using IceCrow.Hearthstone.Logs;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Tracking;

namespace IceCrow.Live.Tests;

/// <summary>
/// Fail-closed completeness: when the bounded pre-start buffer dropped events
/// before confirmation, the candidate is incomplete evidence and must never
/// become an authoritative match or capture. A truncated late-attach match is
/// sacrificed rather than published as falsely complete.
/// </summary>
public sealed class LifecycleCompletenessTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        17,
        12,
        0,
        0,
        TimeSpan.Zero);

    private int _lineIndex;

    [Fact]
    public void OverflowBeforeConfirmationRejectsTheCandidateEntirely()
    {
        var observer = new CountingObserver();
        var coordinator = new LiveTrackingCoordinator(
            pendingEventCapacity: 3,
            appliedEventObserver: observer);

        _ = coordinator.Process(Line("CREATE_GAME"));
        _ = coordinator.Process(Line("GameEntity EntityID=500"));
        _ = coordinator.Process(Line("Player EntityID=1 PlayerID=1 GameAccountId=[hi=0 lo=1]"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=1"));
        _ = coordinator.Process(Line("Player EntityID=2 PlayerID=2 GameAccountId=[hi=0 lo=2]"));
        var confirming = coordinator.Process(Line("TAG_CHANGE Entity=500 tag=STEP value=BEGIN_MULLIGAN"));

        Assert.False(confirming.StateChanged);
        Assert.Equal(TrackingSessionState.Inactive, coordinator.CurrentSnapshot.SessionState);
        Assert.Equal(0, observer.Starts);
        var diagnostics = coordinator.Diagnostics;
        Assert.Equal(1, diagnostics.IncompleteCandidateRejections);
        Assert.True(diagnostics.CandidateBufferedDrops > 0);
        Assert.Equal(0, diagnostics.PendingPreStartEvents);
        Assert.True(diagnostics.Warnings.HasFlag(
            LiveTrackingWarnings.IncompleteCandidateEvidence));

        // Later strong events must not resurrect the rejected candidate.
        var later = coordinator.Process(Line("TAG_CHANGE Entity=500 tag=TURN value=1"));
        Assert.False(later.StateChanged);
        Assert.Equal(0, observer.Starts);
        Assert.Equal(1, coordinator.Diagnostics.IncompleteCandidateRejections);
    }

    [Fact]
    public void NewGameBoundaryRecoversNormalOperationAfterRejection()
    {
        var observer = new CountingObserver();
        var coordinator = new LiveTrackingCoordinator(
            pendingEventCapacity: 3,
            appliedEventObserver: observer);

        // First candidate overflows and is rejected.
        _ = coordinator.Process(Line("CREATE_GAME"));
        _ = coordinator.Process(Line("GameEntity EntityID=500"));
        _ = coordinator.Process(Line("Player EntityID=1 PlayerID=1 GameAccountId=[hi=0 lo=1]"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=1"));
        _ = coordinator.Process(Line("Player EntityID=2 PlayerID=2 GameAccountId=[hi=0 lo=2]"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=500 tag=STEP value=BEGIN_MULLIGAN"));
        Assert.Equal(0, observer.Starts);

        // The next boundary resets candidate-local state; a complete
        // candidate below capacity starts exactly one match.
        _ = coordinator.Process(Line("CREATE_GAME"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=1"));
        var started = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=STEP value=BEGIN_MULLIGAN"));

        Assert.True(started.StateChanged);
        Assert.Equal(1, observer.Starts);
        Assert.Equal(TrackingSessionState.Active, coordinator.CurrentSnapshot.SessionState);
        var diagnostics = coordinator.Diagnostics;
        Assert.Equal(0, diagnostics.CandidateBufferedDrops);
        Assert.Equal(1, diagnostics.IncompleteCandidateRejections);
        Assert.Equal(
            BattlegroundsLifecycleEvidence.StepProgress,
            diagnostics.LastConfirmationReason);
        Assert.False(diagnostics.Warnings.HasFlag(
            LiveTrackingWarnings.IncompleteCandidateEvidence));
    }

    [Fact]
    public void DiagnosticsTrackCandidateArmingPendingAndReset()
    {
        var coordinator = new LiveTrackingCoordinator();

        _ = coordinator.Process(Line("CREATE_GAME"));
        Assert.False(coordinator.Diagnostics.LifecycleCandidateArmed);

        _ = coordinator.Process(Line("GameEntity EntityID=500"));
        _ = coordinator.Process(Line("TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=1"));
        var armed = coordinator.Diagnostics;
        Assert.True(armed.LifecycleCandidateArmed);
        Assert.Equal(2, armed.PendingPreStartEvents);
        Assert.Equal(BattlegroundsLifecycleEvidence.None, armed.LastConfirmationReason);

        _ = coordinator.Process(Line("TAG_CHANGE Entity=500 tag=TURN value=1"));
        var started = coordinator.Diagnostics;
        Assert.False(started.LifecycleCandidateArmed);
        Assert.Equal(0, started.PendingPreStartEvents);
        Assert.Equal(
            BattlegroundsLifecycleEvidence.TurnProgress,
            started.LastConfirmationReason);
        Assert.Equal(0, started.CandidateBufferedDrops);
    }

    private RawLogLine Line(string payload) => new(
        Timestamp.AddMilliseconds(_lineIndex++),
        "Power",
        $"PowerTaskList.DebugPrintPower() - {payload}",
        payload);

    private sealed class CountingObserver : IAppliedMatchEventObserver
    {
        public int Starts { get; private set; }

        public void OnMatchStarted(DateTimeOffset timestamp, int? localPlayerId) => Starts++;

        public void OnEventApplied(GameEvent gameEvent)
        {
        }

        public void OnEventRejected(GameEvent gameEvent)
        {
        }

        public void OnMatchEnded(DateTimeOffset timestamp)
        {
        }
    }
}
