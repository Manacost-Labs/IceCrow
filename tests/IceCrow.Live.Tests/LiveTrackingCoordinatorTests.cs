using System.Threading.Channels;
using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Logs;
using IceCrow.Hearthstone.Protocol;
using IceCrow.Tracking;

namespace IceCrow.Live.Tests;

public sealed class LiveTrackingCoordinatorTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        14,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void RawFixtureReconstructsTrackingStateThroughCompleteLivePipeline()
    {
        var coordinator = new LiveTrackingCoordinator();

        foreach (var content in CreateBattlegroundsFixture())
        {
            _ = coordinator.Process(Line(content));
        }

        var snapshot = coordinator.CurrentSnapshot;
        Assert.Equal(TrackingSessionState.Ended, snapshot.SessionState);
        Assert.Equal(BattlegroundsPhase.GameOver, snapshot.Battlegrounds.Phase);
        Assert.Equal(2, snapshot.Battlegrounds.Turn);
        Assert.Equal(1, snapshot.Battlegrounds.LocalPlayerId);
        Assert.Equal(2, snapshot.Battlegrounds.Lobby.Count);
        Assert.Equal(3, snapshot.Battlegrounds.Lobby.GetPlayer(2)?.TavernTier);
        var board = Assert.IsType<BoardSnapshot>(snapshot.OpponentMemory.GetLatest(2));
        Assert.Equal(7, Assert.Single(board.Minions).Attack);
        Assert.Contains(
            snapshot.LobbyTimeline.Events,
            timelineEvent => timelineEvent is OpponentObserved { PlayerId: 2, MinionCount: 1 });
        Assert.Equal(CreateBattlegroundsFixture().Count, coordinator.Diagnostics.RawLinesReceived);
        Assert.True(coordinator.Diagnostics.TrackingEventsApplied > 0);
    }

    [Fact]
    public void OrdinaryGameCreationDoesNotStartBattlegrounds()
    {
        var coordinator = new LiveTrackingCoordinator();

        _ = coordinator.Process(Line("CREATE_GAME"));
        _ = coordinator.Process(Line("GameEntity EntityID=1"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=TURN value=1"));

        Assert.Equal(TrackingSessionState.Inactive, coordinator.CurrentSnapshot.SessionState);
        Assert.False(coordinator.Diagnostics.IsBattlegroundsActive);
        Assert.Equal(0, coordinator.Diagnostics.TrackingEventsApplied);
    }

    [Fact]
    public void UnknownAndMalformedLinesDoNotMutateTrackingState()
    {
        var coordinator = new LiveTrackingCoordinator();

        var unknown = coordinator.Process(Line("META_DATA - Meta=TARGET Data=0 InfoCount=0"));
        var malformed = coordinator.Process(Line("GameEntity EntityID=not-an-id"));

        Assert.Equal(PowerParseStatus.Unknown, unknown.ParseResult.Status);
        Assert.Equal(PowerParseStatus.Malformed, malformed.ParseResult.Status);
        Assert.Equal(TrackingSessionState.Inactive, coordinator.CurrentSnapshot.SessionState);
        Assert.Equal(0, coordinator.Diagnostics.TrackingEventsApplied);
        Assert.Equal(1, coordinator.Diagnostics.Unknown);
        Assert.Equal(1, coordinator.Diagnostics.Malformed);
    }

    [Fact]
    public void NewCreateGameThenBattlegroundsEvidenceClearsPreviousMatch()
    {
        var coordinator = new LiveTrackingCoordinator();
        foreach (var content in CreateBattlegroundsFixture(includeGameEnd: false))
        {
            _ = coordinator.Process(Line(content));
        }

        Assert.NotEmpty(coordinator.CurrentSnapshot.OpponentMemory.Histories);

        _ = coordinator.Process(Line("CREATE_GAME"));
        _ = coordinator.Process(Line("Player EntityID=9 PlayerID=9 GameAccountId=[hi=0 lo=9]"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=9 tag=PLAYER_TECH_LEVEL value=1"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=9 tag=PLAYER_TECH_LEVEL value=2"));

        var snapshot = coordinator.CurrentSnapshot;
        Assert.Equal(TrackingSessionState.Active, snapshot.SessionState);
        Assert.Equal(1, snapshot.EntityCount);
        Assert.Empty(snapshot.OpponentMemory.Histories);
        Assert.Empty(snapshot.LobbyTimeline.Events.OfType<OpponentObserved>());
    }

    [Fact]
    public void CompleteGameStateEndsActiveMatch()
    {
        var coordinator = new LiveTrackingCoordinator();
        _ = coordinator.Process(Line("CREATE_GAME"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=1"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=PLAYER_TRIPLES value=0"));

        var update = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=STATE value=COMPLETE"));

        Assert.True(update.StateChanged);
        Assert.Equal(TrackingSessionState.Ended, update.Snapshot?.SessionState);
        Assert.Equal(BattlegroundsPhase.GameOver, update.Snapshot?.Battlegrounds.Phase);
    }

    [Fact]
    public void PendingEventsAreBoundedAndUseDropOldestPolicy()
    {
        var coordinator = new LiveTrackingCoordinator(pendingEventCapacity: 3);

        _ = coordinator.Process(Line("GameEntity EntityID=1"));
        _ = coordinator.Process(Line("GameEntity EntityID=2"));
        _ = coordinator.Process(Line("GameEntity EntityID=3"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=3 tag=PLAYER_TECH_LEVEL value=1"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=3 tag=PLAYER_TRIPLES value=0"));

        Assert.Equal(2, coordinator.Diagnostics.BufferedEventsDropped);
        Assert.Equal(1, coordinator.CurrentSnapshot.EntityCount);
    }

    [Fact]
    public async Task RunAsyncStopsCleanlyWhenCancellationIsRequested()
    {
        var coordinator = new LiveTrackingCoordinator();
        var channel = Channel.CreateBounded<RawLogLine>(1);
        using var cancellation = new CancellationTokenSource();
        var runTask = coordinator.RunAsync(channel.Reader, cancellationToken: cancellation.Token);
        await channel.Writer.WriteAsync(Line("CREATE_GAME"));

        cancellation.Cancel();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(runTask.IsCompletedSuccessfully);
    }

    [Fact]
    public void LiveSafetyWarningsAndHardRejectionsAreObservable()
    {
        var tracking = new TrackingSession(new TrackingSessionLimits(
            maximumTrackedEntities: 4,
            trackedEntityWarningThreshold: 1,
            maximumTagsPerEntity: 2,
            tagsPerEntityWarningThreshold: 1,
            maximumLobbyPlayers: 2,
            lobbyPlayerWarningThreshold: 1,
            maximumTimelineEventsPerPlayer: 4,
            timelineEventWarningThreshold: 1,
            maximumOpponentSnapshotsPerPlayer: 4,
            opponentSnapshotWarningThreshold: 1));
        var coordinator = new LiveTrackingCoordinator(tracking: tracking);

        _ = coordinator.Process(Line("CREATE_GAME"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=1"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=STEP value=MAIN_READY"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=1000 value=1"));
        var rejected = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=1001 value=1"));

        Assert.Equal(1, rejected.Diagnostics.SafetyLimitRejections);
        Assert.True(rejected.Diagnostics.Warnings.HasFlag(LiveTrackingWarnings.TrackedEntities));
        Assert.True(rejected.Diagnostics.Warnings.HasFlag(LiveTrackingWarnings.TagsPerEntity));
        Assert.Equal(2, rejected.Diagnostics.MaximumTagsOnEntity);
    }

    [Fact]
    public void UnresolvedNamedReferencesAreCountedFlaggedAndResetPerMatch()
    {
        var coordinator = new LiveTrackingCoordinator();

        // Confirmed match with a declared game entity.
        _ = coordinator.Process(Line("CREATE_GAME"));
        _ = coordinator.Process(Line("GameEntity EntityID=500"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=1"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=STEP value=MAIN_READY"));

        // A resolvable bare-name reference does not count as unresolved...
        var resolved = coordinator.Process(Line("TAG_CHANGE Entity=GameEntity tag=TURN value=1"));
        Assert.Equal(0, resolved.Diagnostics.UnresolvedNamedReferences);
        Assert.False(resolved.Diagnostics.Warnings.HasFlag(
            LiveTrackingWarnings.UnresolvedNamedReferences));

        // ...while an unknown bare name is counted and flagged, never guessed.
        var unresolved = coordinator.Process(Line("TAG_CHANGE Entity=Mystery tag=ATK value=5"));
        Assert.Equal(1, unresolved.Diagnostics.UnresolvedNamedReferences);
        Assert.True(unresolved.Diagnostics.Warnings.HasFlag(
            LiveTrackingWarnings.UnresolvedNamedReferences));

        // A new confirmed match resets the counter with the match state.
        _ = coordinator.Process(Line("CREATE_GAME"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=1"));
        var secondMatch = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=STEP value=MAIN_READY"));
        Assert.Equal(0, secondMatch.Diagnostics.UnresolvedNamedReferences);
    }

    [Fact]
    public void LifecyclePlayerIdentityCacheUsesBoundedEviction()
    {
        var lifecycle = new BattlegroundsLifecycleDetector(
            maximumPlayerIdentities: 2,
            playerIdentityWarningThreshold: 1);

        for (var player = 1; player <= 3; player++)
        {
            _ = lifecycle.Observe(new IceCrow.Hearthstone.Protocol.Events.PlayerEntityDeclared(
                Timestamp,
                BlockId: null,
                EntityId: player,
                PlayerId: player,
                GameAccountId: $"account-{player}"));
        }

        Assert.Equal(2, lifecycle.PlayerIdentityCount);
        Assert.Equal(1, lifecycle.PlayerIdentityEvictions);
    }

    private int _lineIndex;

    // Real Power.log lines carry advancing timestamps; a same-timestamp burst
    // is catch-up snapshot territory covered by the lifecycle confirmation
    // tests, so every fed line here advances log time by one millisecond.
    private RawLogLine Line(string payload) => new(
        Timestamp.AddMilliseconds(_lineIndex++),
        "Power",
        $"PowerTaskList.DebugPrintPower() - {payload}",
        payload);

    private static List<string> CreateBattlegroundsFixture(bool includeGameEnd = true)
    {
        var lines = new List<string>
        {
            "CREATE_GAME",
            "GameEntity EntityID=500",
            "Player EntityID=1 PlayerID=1 GameAccountId=[hi=0 lo=1]",
            "Player EntityID=2 PlayerID=2 GameAccountId=[hi=0 lo=2]",
            "TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=2",
            "TAG_CHANGE Entity=1 tag=NEXT_OPPONENT_PLAYER_ID value=2",
            "TAG_CHANGE Entity=2 tag=PLAYER_TECH_LEVEL value=3",
            "FULL_ENTITY - Creating ID=201 CardID=BG_MINION_001",
            "tag=CARDTYPE value=MINION",
            "tag=ZONE value=PLAY",
            "tag=CONTROLLER value=2",
            "tag=ZONE_POSITION value=1",
            "tag=ATK value=7",
            "tag=HEALTH value=8",
            "TAG_CHANGE Entity=500 tag=TURN value=3",
            "TAG_CHANGE Entity=500 tag=2022 value=1",
            "TAG_CHANGE Entity=500 tag=2022 value=0",
        };
        if (includeGameEnd)
        {
            lines.Add("TAG_CHANGE Entity=1 tag=PLAYSTATE value=WON");
        }

        return lines;
    }
}
