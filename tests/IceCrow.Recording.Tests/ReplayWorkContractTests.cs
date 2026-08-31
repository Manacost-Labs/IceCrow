using System.Globalization;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Recording.Tests;

/// <summary>
/// Contracts for the real-match-calibrated event-snapshot work budget. The
/// generated stream mirrors the measured shape of the four 2026-08-31 full
/// captures: RawTagChanged-heavy, a bounded entity population where most
/// touched entities hold a handful of tags and one hot game-entity carries a
/// large tag set, averaging roughly 10-13 work units per event.
/// </summary>
public sealed class ReplayWorkContractTests
{
    private const int FullCapacityEventCount = RecordingSerializer.MaximumEventCount;
    private const int ProportionalEventCount = FullCapacityEventCount / 10;

    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        31,
        14,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void RealisticProportionalReplayFitsTheBudgetWithFullCapacityHeadroom()
    {
        var match = CreateRealisticMatch(ProportionalEventCount);
        var runner = new ReplayRunner(match);

        var state = runner.RunAll();

        var work = runner.WorkDiagnostics;
        Assert.Equal(match.Events.Count, state.ProcessedEventCount);
        Assert.True(work.EventSnapshotWorkUnits > 0);

        // Proportionality: scaling this shape to the full 250k event budget
        // must still fit the default event-snapshot budget.
        Assert.True(
            work.EventSnapshotWorkUnits * (long)(FullCapacityEventCount / ProportionalEventCount)
                <= ReplayLimits.DefaultMaximumEventSnapshotWorkUnits,
            $"{work.EventSnapshotWorkUnits} work for {ProportionalEventCount} events would " +
            "exceed the default budget at full capacity.");
    }

    [Fact]
    [Trait("Category", "Soak")]
    public void FullCapacityRealisticReplayPassesUnderDefaultLimits()
    {
        var match = CreateRealisticMatch(FullCapacityEventCount);
        var runner = new ReplayRunner(match);

        var state = runner.RunAll();

        var work = runner.WorkDiagnostics;
        Assert.Equal(FullCapacityEventCount, state.ProcessedEventCount);
        Assert.True(
            work.EventSnapshotWorkUnits <= ReplayLimits.DefaultMaximumEventSnapshotWorkUnits);
        Assert.True(
            work.TimelineWorkUnits <= ReplayLimits.DefaultMaximumTimelineWorkUnits);
    }

    [Fact]
    public void StrictEventSnapshotBudgetStillRejectsTheRealisticShape()
    {
        var match = CreateRealisticMatch(ProportionalEventCount);
        var strict = new ReplayRunner(
            match,
            new ReplayLimits(MaximumEventSnapshotWorkUnits: 10_000));

        var exception = Assert.Throws<InvalidDataException>(() => strict.RunAll());

        Assert.Contains("event snapshot work-unit", exception.Message, StringComparison.Ordinal);
    }

    private static RecordedMatch CreateRealisticMatch(int totalEventCount)
    {
        const int gameEntityId = 1;
        const int minionPopulation = 300;
        var recorder = new MatchRecorder(Timestamp);
        recorder.RecordMatchStarted(Timestamp, localPlayerId: 1);

        // 1 game entity accumulating a large tag set (hot entity, ~40 tags)
        // plus a rotating minion population holding 8 distinct tags each.
        var bodyEvents = totalEventCount - 2;
        for (var index = 0; index < bodyEvents; index++)
        {
            var hitsGameEntity = index % 10 == 0;
            var entityId = hitsGameEntity
                ? gameEntityId
                : 2 + (index % minionPopulation);
            var tag = hitsGameEntity
                ? (2000 + (index / 10 % 40)).ToString(CultureInfo.InvariantCulture)
                : (100 + (index % 8)).ToString(CultureInfo.InvariantCulture);
            recorder.Record(new RawTagChanged(
                Timestamp.AddMilliseconds(index),
                BlockId: null,
                EntityId: entityId,
                EntityName: null,
                Tag: tag,
                Value: (index % 7).ToString(CultureInfo.InvariantCulture),
                IsCreationTag: false));
        }

        recorder.RecordMatchEnded(Timestamp.AddHours(1));
        return recorder.CreateMatch();
    }
}
