using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Tests;

/// <summary>
/// The durable handoff invariant: an accepted record is owned by the worker
/// until the outbox commits it; transient IO failures are retried, a full
/// handoff is refused explicitly, shutdown drains, and nothing persists twice.
/// </summary>
public sealed class ProfilePersistenceWorkerTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "icecrow-handoff-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task LockedOutboxIsRetriedUntilTheLockIsReleasedThenPersistedOnce()
    {
        Directory.CreateDirectory(_directory);
        using var outbox = Outbox();
        await outbox.EnqueueAsync(Match());
        using var worker = Worker(outbox);
        var persisted = new List<Guid>();
        worker.Persisted += item => persisted.Add(item.EventId);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = worker.RunAsync(cancellation.Token);
        var pending = Match();

        using (new FileStream(JournalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(ProfileHandoffResult.Accepted, worker.Accept(pending));
            await WaitForAsync(() => worker.Status.Retries >= 2, cancellation.Token);
            Assert.Equal(ProfileHandoffPhase.Retrying, worker.Status.Phase);
            Assert.Equal("IOException", worker.Status.LastFailure);
            Assert.Equal(1, worker.Status.Pending);
        }

        await WaitForAsync(() => worker.Status.Persisted == 1, cancellation.Token);

        Assert.Equal([pending.EventId], persisted);
        Assert.Equal(2, await outbox.CountAsync());
        worker.Complete();
        await run;
        Assert.Equal(ProfileHandoffPhase.Stopped, worker.Status.Phase);
        Assert.Equal(0, worker.Status.Pending);
    }

    [Fact]
    public async Task FullHandoffIsRefusedExplicitlyAndOldestAcceptedIsPreserved()
    {
        using var outbox = Outbox();
        using var worker = Worker(outbox, capacity: 2);
        var first = Match();
        var second = Match();
        var third = Match();

        Assert.Equal(ProfileHandoffResult.Accepted, worker.Accept(first));
        Assert.Equal(ProfileHandoffResult.Accepted, worker.Accept(second));
        Assert.Equal(ProfileHandoffResult.Full, worker.Accept(third));
        Assert.Equal(ProfileHandoffPhase.Full, worker.Status.Phase);
        Assert.Equal(1, worker.Status.RefusedFull);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = worker.RunAsync(cancellation.Token);
        await WaitForAsync(() => worker.Status.Persisted == 2, cancellation.Token);
        Assert.Equal(ProfileHandoffResult.Accepted, worker.Accept(third));
        worker.Complete();
        await run;

        Assert.Equal([first.EventId, second.EventId, third.EventId], (await outbox.PeekBatchAsync(10)).Select(static item => item.EventId));
    }

    [Fact]
    public async Task ShutdownDrainsEveryAcceptedEventBeforeStopping()
    {
        using var outbox = Outbox();
        using var worker = Worker(outbox, capacity: 16);
        var accepted = Enumerable.Range(0, 10).Select(_ => Match()).ToArray();
        foreach (var item in accepted)
        {
            Assert.Equal(ProfileHandoffResult.Accepted, worker.Accept(item));
        }

        using var cancellation = new CancellationTokenSource();
        var run = worker.RunAsync(cancellation.Token);
        cancellation.Cancel();
        await run;

        Assert.Equal(ProfileHandoffResult.Closed, worker.Accept(Match()));
        Assert.Equal(ProfileHandoffPhase.Stopped, worker.Status.Phase);
        Assert.Null(worker.Status.LastFailure);
        Assert.Equal(accepted.Select(static item => item.EventId), (await outbox.PeekBatchAsync(50)).Select(static item => item.EventId));
    }

    [Fact]
    public async Task RetryAfterALateFailureDoesNotPersistTwice()
    {
        using var outbox = Outbox();
        var match = Match();
        await outbox.EnqueueAsync(match);
        using var worker = Worker(outbox);
        Assert.Equal(ProfileHandoffResult.Accepted, worker.Accept(match));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = worker.RunAsync(cancellation.Token);

        await WaitForAsync(() => worker.Status.Duplicates == 1, cancellation.Token);
        worker.Complete();
        await run;

        Assert.Equal(1, await outbox.CountAsync());
        Assert.Equal(0, worker.Status.Persisted);
    }

    [Fact]
    public async Task OutboxFullIsHeldAndRetriedUntilSpaceReturns()
    {
        using var outbox = new ProfileOutbox(Path.Combine(_directory, "outbox.json"), maximumHistoryItems: 1);
        var blocker = Match();
        await outbox.EnqueueAsync(blocker);
        using var worker = Worker(outbox);
        var held = Match();
        Assert.Equal(ProfileHandoffResult.Accepted, worker.Accept(held));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = worker.RunAsync(cancellation.Token);

        await WaitForAsync(() => worker.Status.Phase == ProfileHandoffPhase.Full && worker.Status.Retries >= 1, cancellation.Token);
        Assert.Equal("outbox full", worker.Status.LastFailure);
        await outbox.RemoveAsync([blocker.EventId]);
        await WaitForAsync(() => worker.Status.Persisted == 1, cancellation.Token);
        worker.Complete();
        await run;

        Assert.Equal(held.EventId, Assert.Single(await outbox.PeekBatchAsync(10)).EventId);
    }

    private string JournalPath => Path.Combine(_directory, "outbox.jsonl");

    private ProfileOutbox Outbox() => new(Path.Combine(_directory, "outbox.json"));

    private static ProfilePersistenceWorker Worker(ProfileOutbox outbox, int capacity = 8) => new(
        outbox,
        capacity,
        minimumRetry: TimeSpan.FromMilliseconds(20),
        maximumRetry: TimeSpan.FromMilliseconds(100),
        drainGrace: TimeSpan.FromSeconds(10));

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    private static ProfileEvent Match() => ProfileEvent.Create(
        ProfileEventType.ConstructedMatch,
        Timestamp,
        new ConstructedMatchRecord(
            Guid.CreateVersion7(), "ranked", "wild", MatchResult.Lost, Certainty.Exact,
            Timestamp, Timestamp.AddMinutes(5), 300, 8, "HERO_01", "HERO_02",
            DeckEvidence.Unknown, MulliganRecord.Unknown, null, OpponentDeckEvidence.Unknown, null, 224857, 2));
}
