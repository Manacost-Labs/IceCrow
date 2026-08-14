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

        var parserElapsed = Measure(ParserIterations, index =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = Parser.Parse(
                $"TAG_CHANGE Entity=1 tag=TURN value={(index % 200) + 1}");
        });

        var tracking = new TrackingSession();
        _ = tracking.StartBattlegroundsMatch(Timestamp);
        var trackingElapsed = Measure(TrackingIterations, index =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = tracking.Apply(Tag(1, "TURN", ((index % 200) + 1).ToString(CultureInfo.InvariantCulture), index));
        });

        var replayMatch = CreateReplayMatch(ReplayIterations);
        var replayStopwatch = Stopwatch.StartNew();
        _ = new ReplayRunner(replayMatch).RunAll(cancellationToken);
        replayStopwatch.Stop();

        var snapshotSession = new TrackingSession();
        _ = snapshotSession.StartBattlegroundsMatch(Timestamp);
        for (var entityId = 1; entityId <= SnapshotEntities; entityId++)
        {
            _ = snapshotSession.Apply(Tag(entityId, "ATK", "1", entityId));
            _ = snapshotSession.Apply(Tag(entityId, "HEALTH", "2", entityId));
            _ = snapshotSession.Apply(Tag(entityId, "ZONE", "PLAY", entityId));
            _ = snapshotSession.Apply(Tag(entityId, "CARDTYPE", "MINION", entityId));
        }

        var snapshotStopwatch = Stopwatch.StartNew();
        var snapshots = snapshotSession.CreateEntitySnapshots();
        snapshotStopwatch.Stop();

        var fixtureStopwatch = Stopwatch.StartNew();
        var fixture = await FixtureGoldenRunner
            .RunAsync(fixturePath, cancellationToken)
            .ConfigureAwait(false);
        fixtureStopwatch.Stop();

        return new HardeningBenchmarkResult(
            ParserIterations,
            parserElapsed,
            TrackingIterations,
            trackingElapsed,
            replayMatch.Events.Count,
            replayStopwatch.Elapsed,
            snapshots.Count,
            snapshotSession.TagCount,
            snapshotStopwatch.Elapsed,
            fixture.FixtureName,
            fixtureStopwatch.Elapsed);
    }

    public static string Format(HardeningBenchmarkResult result) => string.Join(
        Environment.NewLine,
        "IceCrow hardening baseline (diagnostic only; no timing is a CI threshold)",
        $"Power parser: {Rate(result.ParserLines, result.ParserElapsed):F0} lines/s ({result.ParserLines} lines, {result.ParserElapsed.TotalMilliseconds:F2} ms)",
        $"TrackingSession: {Rate(result.TrackingEvents, result.TrackingElapsed):F0} events/s ({result.TrackingEvents} events, {result.TrackingElapsed.TotalMilliseconds:F2} ms)",
        $"ReplayRunner: {Rate(result.ReplayEvents, result.ReplayElapsed):F0} events/s ({result.ReplayEvents} events, {result.ReplayElapsed.TotalMilliseconds:F2} ms)",
        $"Entity snapshots: {result.SnapshotEntities} entities / {result.SnapshotTags} tags in {result.SnapshotElapsed.TotalMilliseconds:F2} ms",
        $"Full fixture '{result.FixtureName}': {result.FixtureElapsed.TotalMilliseconds:F2} ms");

    private static readonly PowerLineParser Parser = new();

    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        14,
        12,
        0,
        0,
        TimeSpan.Zero);

    private static TimeSpan Measure(int iterations, Action<int> action)
    {
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < iterations; index++)
        {
            action(index);
        }

        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private static double Rate(int operations, TimeSpan elapsed) =>
        operations / Math.Max(elapsed.TotalSeconds, double.Epsilon);

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
}

public sealed record HardeningBenchmarkResult(
    int ParserLines,
    TimeSpan ParserElapsed,
    int TrackingEvents,
    TimeSpan TrackingElapsed,
    int ReplayEvents,
    TimeSpan ReplayElapsed,
    int SnapshotEntities,
    int SnapshotTags,
    TimeSpan SnapshotElapsed,
    string FixtureName,
    TimeSpan FixtureElapsed);
