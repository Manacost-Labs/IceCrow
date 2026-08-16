using System.Globalization;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Recording.Tests;

/// <summary>
/// Timeline work accounting must charge actual work (one unit per event plus
/// newly added timeline entries), not the full retained history after every
/// event — the old model rejected a real half-match at ~26 K events.
/// </summary>
public sealed class ReplayTimelineWorkTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        16,
        22,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void RealisticTimelineHeavyMatchReplaysWithLinearWork()
    {
        // Eight players keep toggling tavern tiers so the retained timeline
        // stays saturated. Under the old accounting this charges roughly
        // events x retained-history and fails long before the end.
        var recorder = new MatchRecorder(Timestamp);
        recorder.RecordMatchStarted(Timestamp, localPlayerId: 1);
        Declare(recorder);
        for (var index = 0; index < 30_000; index++)
        {
            var playerId = (index % 8) + 1;
            recorder.Record(Tag(
                playerId,
                "PLAYER_TECH_LEVEL",
                ((index % 5) + 1).ToString(CultureInfo.InvariantCulture),
                index));
        }

        recorder.RecordMatchEnded(Timestamp.AddHours(1));

        var match = recorder.CreateMatch();
        var state = new ReplayRunner(match).RunAll();

        Assert.Equal(match.Events.Count, state.ProcessedEventCount);
    }

    [Fact]
    public void TimelineWorkBoundaryStillRejectsExcessiveWork()
    {
        var recorder = new MatchRecorder(Timestamp);
        recorder.RecordMatchStarted(Timestamp, localPlayerId: 1);
        Declare(recorder);
        for (var index = 0; index < 64; index++)
        {
            recorder.Record(Tag(
                1,
                "PLAYER_TECH_LEVEL",
                ((index % 5) + 1).ToString(CultureInfo.InvariantCulture),
                index));
        }

        recorder.RecordMatchEnded(Timestamp.AddHours(1));
        var runner = new ReplayRunner(
            recorder.CreateMatch(),
            new ReplayLimits(MaximumTimelineWorkUnits: 16));

        Assert.Throws<InvalidDataException>(() => runner.RunAll());
    }

    [Fact]
    public void ResetClearsTimelineWorkAccounting()
    {
        var recorder = new MatchRecorder(Timestamp);
        recorder.RecordMatchStarted(Timestamp, localPlayerId: 1);
        Declare(recorder);
        for (var index = 0; index < 32; index++)
        {
            recorder.Record(Tag(
                1,
                "PLAYER_TECH_LEVEL",
                ((index % 5) + 1).ToString(CultureInfo.InvariantCulture),
                index));
        }

        recorder.RecordMatchEnded(Timestamp.AddHours(1));
        var runner = new ReplayRunner(recorder.CreateMatch());

        var first = runner.RunAll();
        runner.Reset();
        var second = runner.RunAll();

        Assert.Equal(first.ProcessedEventCount, second.ProcessedEventCount);
    }

    private static void Declare(MatchRecorder recorder)
    {
        for (var playerId = 1; playerId <= 8; playerId++)
        {
            recorder.Record(new PlayerEntityDeclared(
                Timestamp,
                BlockId: null,
                EntityId: playerId,
                PlayerId: playerId,
                GameAccountId: $"account-{playerId}"));
        }
    }

    private static RawTagChanged Tag(int entityId, string tag, string value, int index) => new(
        Timestamp.AddMilliseconds(index),
        BlockId: null,
        EntityId: entityId,
        EntityName: null,
        Tag: tag,
        Value: value,
        IsCreationTag: false);
}
