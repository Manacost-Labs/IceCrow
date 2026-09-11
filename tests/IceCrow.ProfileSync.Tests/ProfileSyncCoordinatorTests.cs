using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Tests;

public sealed class ProfileSyncCoordinatorTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProfileSyncOptions FastOptions = new(
        BatchSize: 2,
        IdleInterval: TimeSpan.FromMilliseconds(50),
        MinimumBackoff: TimeSpan.FromMilliseconds(20),
        MaximumBackoff: TimeSpan.FromMilliseconds(200));

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "icecrow-profile-sync-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileOutbox _outbox;
    private int _nextMatch;

    public ProfileSyncCoordinatorTests()
    {
        _outbox = new ProfileOutbox(Path.Combine(_directory, "outbox.json"));
    }

    public void Dispose()
    {
        _outbox.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task PartialAcknowledgementKeepsUnacknowledgedEventsForRetry()
    {
        var first = await EnqueueMatchAsync();
        var second = await EnqueueMatchAsync();
        var transport = new ScriptedTransport(
            batch => new ProfileUploadResult(ProfileUploadStatus.Accepted, [batch[0].EventId], []),
            batch => new ProfileUploadResult(ProfileUploadStatus.Accepted, batch.Select(static item => item.EventId).ToArray(), []));
        using var coordinator = Create(transport, linked: true);

        await coordinator.UploadPendingAsync(CancellationToken.None);

        Assert.Equal(2, transport.Calls.Count);
        Assert.Equal([first, second], transport.Calls[0]);
        Assert.Equal([second], transport.Calls[1]);
        Assert.Equal(0, await _outbox.CountAsync());
        Assert.Equal(2, coordinator.Status.UploadedEvents);
        Assert.Equal(ProfileSyncPhase.Idle, coordinator.Status.Phase);
    }

    [Fact]
    public async Task PermanentRejectionsAreRemovedAndCountedNeverRetried()
    {
        var accepted = await EnqueueMatchAsync();
        var rejected = await EnqueueMatchAsync();
        var transport = new ScriptedTransport(
            _ => new ProfileUploadResult(ProfileUploadStatus.Accepted, [accepted], [rejected]));
        using var coordinator = Create(transport, linked: true);

        await coordinator.UploadPendingAsync(CancellationToken.None);

        Assert.Equal(0, await _outbox.CountAsync());
        Assert.Equal(1, coordinator.Status.UploadedEvents);
        Assert.Equal(1, coordinator.Status.PermanentlyRejectedEvents);
    }

    [Fact]
    public async Task UnauthorizedStopsUploadsUntilTheDeviceIsLinkedAgain()
    {
        await EnqueueMatchAsync();
        var transport = new ScriptedTransport(
            _ => ProfileUploadResult.Unauthorized(),
            batch => new ProfileUploadResult(ProfileUploadStatus.Accepted, batch.Select(static item => item.EventId).ToArray(), []));
        using var coordinator = Create(transport, linked: true);

        await coordinator.UploadPendingAsync(CancellationToken.None);
        Assert.Equal(ProfileSyncPhase.AuthorizationRequired, coordinator.Status.Phase);
        await coordinator.UploadPendingAsync(CancellationToken.None);
        Assert.Single(transport.Calls);
        Assert.Equal(1, await _outbox.CountAsync());

        coordinator.ResetAuthorization();
        await coordinator.UploadPendingAsync(CancellationToken.None);

        Assert.Equal(2, transport.Calls.Count);
        Assert.Equal(0, await _outbox.CountAsync());
    }

    [Fact]
    public async Task UnavailableBacksOffAndRetriesTheSameEventIds()
    {
        var id = await EnqueueMatchAsync();
        var transport = new ScriptedTransport(
            _ => ProfileUploadResult.Unavailable(),
            batch => new ProfileUploadResult(ProfileUploadStatus.Accepted, batch.Select(static item => item.EventId).ToArray(), []));
        var clock = new FakeClock(Timestamp);
        using var coordinator = Create(transport, linked: true, clock);

        await coordinator.UploadPendingAsync(CancellationToken.None);
        Assert.Equal(ProfileSyncPhase.BackingOff, coordinator.Status.Phase);
        Assert.Equal(1, coordinator.Status.ConsecutiveFailures);
        Assert.NotNull(coordinator.Status.RetryAt);

        // Before the retry time nothing is sent; after it the same event id is retried.
        await coordinator.UploadPendingAsync(CancellationToken.None);
        Assert.Single(transport.Calls);
        clock.Advance(TimeSpan.FromSeconds(1));
        await coordinator.UploadPendingAsync(CancellationToken.None);

        Assert.Equal(2, transport.Calls.Count);
        Assert.Equal([id], transport.Calls[1]);
        Assert.Equal(0, coordinator.Status.ConsecutiveFailures);
    }

    [Fact]
    public async Task RateLimitedHonoursRetryAfter()
    {
        await EnqueueMatchAsync();
        var transport = new ScriptedTransport(_ => ProfileUploadResult.RateLimited(TimeSpan.FromMinutes(10)));
        var clock = new FakeClock(Timestamp);
        using var coordinator = Create(transport, linked: true, clock);

        await coordinator.UploadPendingAsync(CancellationToken.None);

        Assert.Equal(Timestamp + TimeSpan.FromMinutes(10), coordinator.Status.RetryAt);
        Assert.Equal(1, await _outbox.CountAsync());
    }

    [Fact]
    public void BackoffGrowsExponentiallyWithinBounds()
    {
        using var coordinator = Create(new ScriptedTransport(), linked: true);

        var first = coordinator.ComputeBackoff(1);
        var third = coordinator.ComputeBackoff(3);
        var huge = coordinator.ComputeBackoff(40);

        Assert.InRange(first, FastOptions.EffectiveMinimumBackoff, TimeSpan.FromMilliseconds(24));
        Assert.InRange(third, TimeSpan.FromMilliseconds(64), TimeSpan.FromMilliseconds(96));
        Assert.InRange(huge, TimeSpan.FromMilliseconds(160), FastOptions.EffectiveMaximumBackoff);
    }

    [Fact]
    public async Task NotLinkedNeverTouchesTheTransport()
    {
        await EnqueueMatchAsync();
        var transport = new ScriptedTransport();
        using var coordinator = Create(transport, linked: false);

        await coordinator.UploadPendingAsync(CancellationToken.None);

        Assert.Empty(transport.Calls);
        Assert.Equal(ProfileSyncPhase.NotLinked, coordinator.Status.Phase);
        Assert.Equal(1, coordinator.Status.PendingEvents);
    }

    [Fact]
    public async Task UploadsAreHeldWhileGameplayIsActive()
    {
        await EnqueueMatchAsync();
        var transport = new ScriptedTransport(
            batch => new ProfileUploadResult(ProfileUploadStatus.Accepted, batch.Select(static item => item.EventId).ToArray(), []));
        using var coordinator = Create(transport, linked: true);
        coordinator.SetGameplayActive(true);

        await coordinator.UploadPendingAsync(CancellationToken.None);
        Assert.Empty(transport.Calls);

        coordinator.SetGameplayActive(false);
        await coordinator.UploadPendingAsync(CancellationToken.None);
        Assert.Single(transport.Calls);
    }

    [Fact]
    public async Task CorruptOutboxBacksOffInsteadOfKillingTheUploader()
    {
        Directory.CreateDirectory(_directory);
        var journalPath = Path.Combine(_directory, "outbox.jsonl");
        using (var seed = new ProfileOutbox(Path.Combine(_directory, "outbox.json")))
        {
            await seed.EnqueueAsync(CreateMatch(0));
        }

        var validLine = await File.ReadAllTextAsync(journalPath);
        await File.WriteAllTextAsync(journalPath, "{ definitely not a journal line\n" + validLine);
        var transport = new ScriptedTransport(
            batch => new ProfileUploadResult(ProfileUploadStatus.Accepted, batch.Select(static item => item.EventId).ToArray(), []));
        using var coordinator = Create(transport, linked: true);

        await coordinator.UploadPendingSafelyAsync(CancellationToken.None);

        Assert.Equal(ProfileSyncPhase.BackingOff, coordinator.Status.Phase);
        Assert.Equal(1, coordinator.Status.ConsecutiveFailures);
        Assert.Empty(transport.Calls);
        Assert.NotNull(coordinator.Status.RetryAt);
    }

    [Fact]
    public async Task RunLoopWakesOnNotifyAndDrainsTheOutbox()
    {
        var transport = new ScriptedTransport(
            batch => new ProfileUploadResult(ProfileUploadStatus.Accepted, batch.Select(static item => item.EventId).ToArray(), []));
        using var coordinator = Create(transport, linked: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var uploaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StatusChanged += status =>
        {
            if (status.UploadedEvents == 1)
            {
                uploaded.TrySetResult();
            }
        };
        var run = coordinator.RunAsync(cancellation.Token);

        await EnqueueMatchAsync();
        coordinator.Notify();
        await uploaded.Task.WaitAsync(cancellation.Token);

        cancellation.Cancel();
        await run;
        Assert.Equal(0, await _outbox.CountAsync());
    }

    private ProfileSyncCoordinator Create(ScriptedTransport transport, bool linked, TimeProvider? clock = null) =>
        new(_outbox, transport, _ => ValueTask.FromResult(linked), FastOptions, clock);

    private async Task<Guid> EnqueueMatchAsync()
    {
        var profileEvent = CreateMatch(_nextMatch++);
        Assert.Equal(ProfileOutboxResult.Enqueued, await _outbox.EnqueueAsync(profileEvent));
        return profileEvent.EventId;
    }

    private static ProfileEvent CreateMatch(int startOffsetMinutes)
    {
        var startedAt = Timestamp.AddMinutes(startOffsetMinutes);
        return ProfileEvent.Create(
            ProfileEventType.ConstructedMatch,
            startedAt,
            new ConstructedMatchRecord(
                Guid.CreateVersion7(),
                "ranked",
                "standard",
                MatchResult.Won,
                Certainty.Exact,
                startedAt,
                startedAt.AddMinutes(8),
                480,
                12,
                "HERO_01",
                "HERO_02",
                DeckEvidence.Unknown,
                MulliganRecord.Unknown,
                null,
                OpponentDeckEvidence.Unknown,
                null,
                224857,
                2));
    }

    private sealed class ScriptedTransport(params Func<IReadOnlyList<ProfileEvent>, ProfileUploadResult>[] responses)
        : IProfileSyncTransport
    {
        public List<Guid[]> Calls { get; } = [];

        public Task<ProfileUploadResult> UploadAsync(IReadOnlyList<ProfileEvent> events, CancellationToken cancellationToken)
        {
            Calls.Add(events.Select(static item => item.EventId).ToArray());
            var index = Math.Min(Calls.Count - 1, responses.Length - 1);
            return Task.FromResult(responses[index](events));
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
