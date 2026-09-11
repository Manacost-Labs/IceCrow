using IceCrow.ProfileSync.History;
using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Tests;

public sealed class ProfileHistoryWorkerTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "icecrow-history-worker-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptedEventsDrainToDurableHistoryAndPublishImmutableSnapshots()
    {
        using var store = new ProfileHistoryStore(Path.Combine(_directory, "matches.jsonl"));
        using var worker = new ProfileHistoryWorker(store, capacity: 4, retryDelay: TimeSpan.FromMilliseconds(10));
        var snapshots = new List<ProfileHistorySnapshot>();
        worker.SnapshotChanged += snapshots.Add;
        var first = Match(Timestamp.AddMinutes(1));
        var second = Match(Timestamp.AddMinutes(2));

        Assert.Equal(ProfileHandoffResult.Accepted, worker.Accept(first));
        Assert.Equal(ProfileHandoffResult.Accepted, worker.Accept(second));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = worker.RunAsync(cancellation.Token);
        worker.Complete();
        await run;

        var final = await store.ReadAsync();
        Assert.Equal(2, final.Matches.Length);
        Assert.Contains(snapshots, static snapshot => snapshot.Matches.Length == 2);
        Assert.Equal(first.EventId, final.Matches[1].EventId);
        Assert.Equal(second.EventId, final.Matches[0].EventId);
    }

    [Fact]
    public async Task FullHandoffRefusesNewEventWithoutReplacingAcceptedEvent()
    {
        using var store = new ProfileHistoryStore(Path.Combine(_directory, "matches.jsonl"));
        using var worker = new ProfileHistoryWorker(store, capacity: 1, retryDelay: TimeSpan.FromMilliseconds(10));
        var accepted = Match(Timestamp.AddMinutes(1));

        Assert.Equal(ProfileHandoffResult.Accepted, worker.Accept(accepted));
        Assert.Equal(ProfileHandoffResult.Full, worker.Accept(Match(Timestamp.AddMinutes(2))));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = worker.RunAsync(cancellation.Token);
        worker.Complete();
        await run;

        Assert.Equal(accepted.EventId, Assert.Single((await store.ReadAsync()).Matches).EventId);
        Assert.Equal(ProfileHandoffResult.Closed, worker.Accept(Match(Timestamp.AddMinutes(3))));
    }

    [Fact]
    public async Task LockedHistoryIsReportedAndRetriedUntilItCanBePersisted()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "matches.jsonl");
        await File.WriteAllTextAsync(path, string.Empty);
        using var store = new ProfileHistoryStore(path);
        using var worker = new ProfileHistoryWorker(store, capacity: 2, retryDelay: TimeSpan.FromMilliseconds(20));
        var failure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.PersistenceUnavailable += value => failure.TrySetResult(value);
        var profileEvent = Match(Timestamp.AddMinutes(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            Assert.Equal(ProfileHandoffResult.Accepted, worker.Accept(profileEvent));
            var run = worker.RunAsync(cancellation.Token);
            Assert.Equal("IOException", await failure.Task.WaitAsync(cancellation.Token));
            await locked.DisposeAsync();
            worker.Complete();
            await run;
        }
        finally
        {
            await locked.DisposeAsync();
        }

        Assert.Equal(profileEvent.EventId, Assert.Single((await store.ReadAsync()).Matches).EventId);
    }

    private static ProfileEvent Match(DateTimeOffset occurredAt) => ProfileEvent.Create(
        ProfileEventType.ConstructedMatch,
        occurredAt,
        new ConstructedMatchRecord(
            Guid.CreateVersion7(), "ranked", "standard", MatchResult.Won, Certainty.Exact,
            Timestamp, occurredAt, (int)(occurredAt - Timestamp).TotalSeconds, 7, "HERO_01", "HERO_02",
            DeckEvidence.Unknown, MulliganRecord.Unknown, null, OpponentDeckEvidence.Unknown, null, 224857, 2));
}
