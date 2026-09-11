using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Tests;

/// <summary>
/// Offline then restart: a match completed while the server is unreachable
/// survives a normal shutdown and a fresh process, then uploads exactly once
/// with its original event id; several offline matches all survive.
/// </summary>
public sealed class CrossRestartDurabilityTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProfileSyncOptions FastOptions = new(
        BatchSize: 10,
        IdleInterval: TimeSpan.FromMilliseconds(50),
        MinimumBackoff: TimeSpan.FromMilliseconds(20),
        MaximumBackoff: TimeSpan.FromMilliseconds(100));

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "icecrow-restart-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task MatchCompletedOfflineUploadsExactlyOnceAfterRestart()
    {
        var offline = new ScriptedTransport(_ => ProfileUploadResult.Unavailable());
        var completed = Match();
        await RunProcessAsync(offline, worker => worker.Accept(completed));
        Assert.Contains(offline.Calls, call => call.Contains(completed.EventId));

        var online = new ScriptedTransport(batch => new ProfileUploadResult(
            ProfileUploadStatus.Accepted, batch.Select(static item => item.EventId).ToArray(), []));
        await RunProcessAsync(online, static _ => ProfileHandoffResult.Accepted, waitForUpload: 1);

        Assert.Equal([completed.EventId], online.Calls.SelectMany(static call => call).Distinct());
        Assert.Single(online.Calls);
        using var outbox = new ProfileOutbox(Path.Combine(_directory, "outbox.json"));
        Assert.Equal(0, await outbox.CountAsync());
    }

    [Fact]
    public async Task SeveralOfflineMatchesAllSurviveRestartWithoutDuplicates()
    {
        var offline = new ScriptedTransport(_ => ProfileUploadResult.Unavailable());
        var matches = Enumerable.Range(0, 5).Select(_ => Match()).ToArray();
        await RunProcessAsync(offline, worker =>
        {
            foreach (var match in matches)
            {
                Assert.Equal(ProfileHandoffResult.Accepted, worker.Accept(match));
            }

            return ProfileHandoffResult.Accepted;
        });

        var online = new ScriptedTransport(batch => new ProfileUploadResult(
            ProfileUploadStatus.Accepted, batch.Select(static item => item.EventId).ToArray(), []));
        await RunProcessAsync(online, static _ => ProfileHandoffResult.Accepted, waitForUpload: 5);

        var uploaded = online.Calls.SelectMany(static call => call).ToArray();
        Assert.Equal(matches.Select(static item => item.EventId).Order(), uploaded.Order());
        Assert.Equal(uploaded.Length, uploaded.Distinct().Count());
    }

    /// <summary>One simulated process lifetime over the shared data directory.</summary>
    private async Task RunProcessAsync(
        ScriptedTransport transport,
        Func<ProfilePersistenceWorker, ProfileHandoffResult> produce,
        int waitForUpload = 0)
    {
        using var outbox = new ProfileOutbox(Path.Combine(_directory, "outbox.json"));
        using var worker = new ProfilePersistenceWorker(outbox, minimumRetry: TimeSpan.FromMilliseconds(20), maximumRetry: TimeSpan.FromMilliseconds(100));
        using var coordinator = new ProfileSyncCoordinator(outbox, transport, static _ => ValueTask.FromResult(true), FastOptions);
        worker.Persisted += _ => coordinator.Notify();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var persistence = worker.RunAsync(cancellation.Token);
        var uploader = coordinator.RunAsync(cancellation.Token);

        produce(worker);
        while (worker.Status.Pending > 0 ||
               coordinator.Status.UploadedEvents < waitForUpload ||
               waitForUpload == 0 && transport.Calls.Count == 0)
        {
            await Task.Delay(20, cancellation.Token);
        }

        // Normal shutdown order: stop producers, drain to durable storage, stop the uploader.
        worker.Complete();
        await persistence;
        cancellation.Cancel();
        await uploader;
    }

    private static ProfileEvent Match() => ProfileEvent.Create(
        ProfileEventType.ConstructedMatch,
        Timestamp,
        new ConstructedMatchRecord(
            Guid.CreateVersion7(), "ranked", "standard", MatchResult.Won, Certainty.Exact,
            Timestamp, Timestamp.AddMinutes(9), 540, 11, "HERO_01", "HERO_02",
            DeckEvidence.Unknown, MulliganRecord.Unknown, null, OpponentDeckEvidence.Unknown, null, 224857, 2));

    private sealed class ScriptedTransport(Func<IReadOnlyList<ProfileEvent>, ProfileUploadResult> respond) : IProfileSyncTransport
    {
        public List<Guid[]> Calls { get; } = [];

        public Task<ProfileUploadResult> UploadAsync(IReadOnlyList<ProfileEvent> events, CancellationToken cancellationToken)
        {
            Calls.Add(events.Select(static item => item.EventId).ToArray());
            return Task.FromResult(respond(events));
        }
    }
}
