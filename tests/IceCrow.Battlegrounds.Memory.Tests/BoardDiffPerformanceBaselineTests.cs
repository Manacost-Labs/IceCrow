using System.Diagnostics;
using System.Globalization;
using IceCrow.Hearthstone.Entities;
using IceCrow.Hearthstone.Protocol.Events;
using Xunit.Abstractions;

namespace IceCrow.Battlegrounds.Memory.Tests;

public sealed class BoardDiffPerformanceBaselineTests(ITestOutputHelper output)
{
    private const int ComparisonCount = 100_000;

    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        15,
        20,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    [Trait("Category", "PerformanceDiagnostic")]
    public void BoardComparisonsProduceAnObservableBaseline()
    {
        // Boards of every realistic size (0-7 minions) with mixed identity
        // outcomes: shared entities, recreated entities, and roster changes.
        var boards = new BoardSnapshot[8];
        for (var size = 0; size < boards.Length; size++)
        {
            boards[size] = CreateBoard(
                turn: size + 3,
                entityBase: size % 2 == 0 ? 100 : 300,
                minionCount: size);
        }

        // Warm up so JIT compilation stays out of the measurement.
        _ = OpponentBoardDiffCalculator.Compare(boards[7], boards[6]);

        var changeTotal = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < ComparisonCount; index++)
        {
            var previous = boards[index % boards.Length];
            var current = boards[(index + 3) % boards.Length];
            var changes = OpponentBoardDiffCalculator.Compare(previous, current);
            changeTotal += changes.AddedMinions.Count +
                changes.RemovedMinions.Count +
                changes.ChangedMinions.Count;
        }

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(changeTotal > 0);
        output.WriteLine(
            "{0:N0} comparisons of 0-7 minion boards: {1:F1}ms total, " +
            "{2:F2}us each; {3:N0} bytes allocated, {4:F0} bytes per comparison.",
            ComparisonCount,
            stopwatch.Elapsed.TotalMilliseconds,
            stopwatch.Elapsed.TotalMilliseconds * 1000 / ComparisonCount,
            allocatedBytes,
            (double)allocatedBytes / ComparisonCount);
    }

    private static BoardSnapshot CreateBoard(int turn, int entityBase, int minionCount)
    {
        var store = new EntityStore();
        for (var position = 1; position <= minionCount; position++)
        {
            var entityId = entityBase + position;
            SetTag(store, entityId, "CARDTYPE", "MINION");
            SetTag(store, entityId, "ZONE", "PLAY");
            SetTag(store, entityId, "CONTROLLER", "2");
            SetTag(
                store,
                entityId,
                "ZONE_POSITION",
                position.ToString(CultureInfo.InvariantCulture));
            SetTag(
                store,
                entityId,
                "ATK",
                (position * turn).ToString(CultureInfo.InvariantCulture));
            SetTag(
                store,
                entityId,
                "HEALTH",
                (position + turn).ToString(CultureInfo.InvariantCulture));
            _ = store.Apply(new EntityRevealed(
                Timestamp,
                BlockId: null,
                EntityId: entityId,
                EntityName: $"Minion {position}",
                CardId: $"BG_CARD_{position}"));
        }

        return BoardSnapshot.Capture(2, turn, Timestamp, store.CreateAllSnapshots());
    }

    private static void SetTag(EntityStore store, int entityId, string tag, string value) =>
        _ = store.Apply(new RawTagChanged(
            Timestamp,
            BlockId: null,
            EntityId: entityId,
            EntityName: null,
            Tag: tag,
            Value: value,
            IsCreationTag: false));
}
