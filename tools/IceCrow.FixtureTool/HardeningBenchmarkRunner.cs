using System.Diagnostics;
using System.Globalization;
using IceCrow.Hearthstone.Protocol;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Recording;
using IceCrow.Tracking;

namespace IceCrow.FixtureTool;

public static class HardeningBenchmarkRunner
{
    private const int ParserIterations = 100_000;
    private const int TrackingIterations = 50_000;
    private const int ReplayIterations = 25_000;
    private const int SnapshotEntities = 2_000;

    public static async Task<HardeningBenchmarkResult> RunAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        var fixturePath = Path.Combine(
            root,
            "tests",
            "fixtures",
            "battlegrounds",
            "synthetic-basic-solo");
        if (!Directory.Exists(fixturePath))
        {
            throw new DirectoryNotFoundException(
                $"Benchmark fixture directory does not exist: {fixturePath}");
        }

        _ = new PowerLineParser().Parse("GameEntity EntityID=1");

        var parserMeasurement = Measure(ParserIterations, index =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = Parser.Parse(
                $"TAG_CHANGE Entity=1 tag=TURN value={(index % 200) + 1}");
        });

        var tracking = new TrackingSession();
        _ = tracking.StartBattlegroundsMatch(Timestamp);
        var trackingMeasurement = Measure(TrackingIterations, index =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = tracking.Apply(Tag(1, "TURN", ((index % 200) + 1).ToString(CultureInfo.InvariantCulture), index));
        });

        var replayMatch = CreateReplayMatch(ReplayIterations);
        var replayAllocationStart = GC.GetTotalAllocatedBytes(precise: true);
        var replayStopwatch = Stopwatch.StartNew();
        _ = new ReplayRunner(replayMatch).RunAll(cancellationToken);
        replayStopwatch.Stop();
        var replayAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - replayAllocationStart;

        var snapshotSession = new TrackingSession();
        _ = snapshotSession.StartBattlegroundsMatch(Timestamp);
        for (var entityId = 1; entityId <= SnapshotEntities; entityId++)
        {
            _ = snapshotSession.Apply(Tag(entityId, "ATK", "1", entityId));
            _ = snapshotSession.Apply(Tag(entityId, "HEALTH", "2", entityId));
            _ = snapshotSession.Apply(Tag(entityId, "ZONE", "PLAY", entityId));
            _ = snapshotSession.Apply(Tag(entityId, "CARDTYPE", "MINION", entityId));
        }

        var snapshotAllocationStart = GC.GetTotalAllocatedBytes(precise: true);
        var snapshotStopwatch = Stopwatch.StartNew();
        var snapshots = snapshotSession.CreateEntitySnapshots();
        snapshotStopwatch.Stop();
        var snapshotAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - snapshotAllocationStart;

        var fixtureAllocationStart = GC.GetTotalAllocatedBytes(precise: true);
        var fixtureStopwatch = Stopwatch.StartNew();
        var fixture = await FixtureGoldenRunner
            .RunAsync(fixturePath, cancellationToken)
            .ConfigureAwait(false);
        fixtureStopwatch.Stop();
        var fixtureAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - fixtureAllocationStart;

        return new HardeningBenchmarkResult(
            ParserIterations,
            parserMeasurement.Elapsed,
            parserMeasurement.AllocatedBytes,
            TrackingIterations,
            trackingMeasurement.Elapsed,
            trackingMeasurement.AllocatedBytes,
            replayMatch.Events.Count,
            replayStopwatch.Elapsed,
            replayAllocatedBytes,
            snapshots.Count,
            snapshotSession.TagCount,
            snapshotStopwatch.Elapsed,
            snapshotAllocatedBytes,
            fixture.FixtureName,
            fixtureStopwatch.Elapsed,
            fixtureAllocatedBytes);
    }

    public static string Format(HardeningBenchmarkResult result) => string.Join(
        Environment.NewLine,
        "IceCrow hardening baseline (diagnostic only; no timing is a CI threshold)",
        $"Power parser: {Rate(result.ParserLines, result.ParserElapsed):F0} lines/s ({result.ParserLines} lines, {result.ParserElapsed.TotalMilliseconds:F2} ms, {PerOperation(result.ParserAllocatedBytes, result.ParserLines):F1} B/line)",
        $"TrackingSession: {Rate(result.TrackingEvents, result.TrackingElapsed):F0} events/s ({result.TrackingEvents} events, {result.TrackingElapsed.TotalMilliseconds:F2} ms, {PerOperation(result.TrackingAllocatedBytes, result.TrackingEvents):F1} B/event)",
        $"ReplayRunner: {Rate(result.ReplayEvents, result.ReplayElapsed):F0} events/s ({result.ReplayEvents} events, {result.ReplayElapsed.TotalMilliseconds:F2} ms, {PerOperation(result.ReplayAllocatedBytes, result.ReplayEvents):F1} B/event)",
        $"Entity snapshots: {result.SnapshotEntities} entities / {result.SnapshotTags} tags in {result.SnapshotElapsed.TotalMilliseconds:F2} ms, {result.SnapshotAllocatedBytes} B allocated",
        $"Full fixture '{result.FixtureName}': {result.FixtureElapsed.TotalMilliseconds:F2} ms, {result.FixtureAllocatedBytes} B allocated");

    private static readonly PowerLineParser Parser = new();

    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        14,
        12,
        0,
        0,
        TimeSpan.Zero);

    private static Measurement Measure(int iterations, Action<int> action)
    {
        var allocationStart = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < iterations; index++)
        {
            action(index);
        }

        stopwatch.Stop();
        return new Measurement(
            stopwatch.Elapsed,
            GC.GetTotalAllocatedBytes(precise: true) - allocationStart);
    }

    private static double Rate(int operations, TimeSpan elapsed) =>
        operations / Math.Max(elapsed.TotalSeconds, double.Epsilon);

    private static double PerOperation(long allocatedBytes, int operations) =>
        (double)allocatedBytes / Math.Max(operations, 1);

    private static RecordedMatch CreateReplayMatch(int normalizedEventCount)
    {
        var events = new RecordedEvent[normalizedEventCount + 2];
        events[0] = RecordedEvent.CreateMatchStarted(Timestamp);
        for (var index = 0; index < normalizedEventCount; index++)
        {
            events[index + 1] = RecordedEvent.FromGameEvent(
                Tag(1, "TURN", ((index % 200) + 1).ToString(CultureInfo.InvariantCulture), index));
        }

        events[^1] = RecordedEvent.CreateMatchEnded(
            Timestamp.AddMilliseconds(normalizedEventCount + 1));
        return new RecordedMatch(
            RecordedMatch.CurrentFormatVersion,
            Timestamp,
            events);
    }

    private static RawTagChanged Tag(
        int entityId,
        string tag,
        string value,
        int millisecond) => new(
        Timestamp.AddMilliseconds(millisecond),
        BlockId: null,
        EntityId: entityId,
        EntityName: null,
        Tag: tag,
        Value: value,
        IsCreationTag: false);

    private readonly record struct Measurement(TimeSpan Elapsed, long AllocatedBytes);
}

public sealed record HardeningBenchmarkResult(
    int ParserLines,
    TimeSpan ParserElapsed,
    long ParserAllocatedBytes,
    int TrackingEvents,
    TimeSpan TrackingElapsed,
    long TrackingAllocatedBytes,
    int ReplayEvents,
    TimeSpan ReplayElapsed,
    long ReplayAllocatedBytes,
    int SnapshotEntities,
    int SnapshotTags,
    TimeSpan SnapshotElapsed,
    long SnapshotAllocatedBytes,
    string FixtureName,
    TimeSpan FixtureElapsed,
    long FixtureAllocatedBytes);
