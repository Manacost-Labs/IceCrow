using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Telemetry;
using IceCrow.Tracking;
using Xunit;

namespace IceCrow.Telemetry.Tests;

public sealed class TelemetryFoundationTests
{
    [Fact]
    public async Task ConsentDefaultsOffAndPreventsPersistence()
    {
        using var temporary = new TemporaryDirectory();
        using var outbox = new TelemetryOutbox(Path.Combine(temporary.Path, "outbox.json"));
        var consent = new TelemetryConsent();

        var queued = await outbox.EnqueueAsync(Summary(Guid.CreateVersion7()), consent);

        Assert.False(consent.IsEnabled);
        Assert.False(queued);
        Assert.Equal(0, await outbox.CountAsync());
    }

    [Fact]
    public async Task PreferencesDefaultOffAndPersistExplicitConsent()
    {
        using var temporary = new TemporaryDirectory();
        var store = new TelemetryPreferencesStore(Path.Combine(temporary.Path, "preferences.json"));

        Assert.False((await store.LoadAsync()).ShareAnonymousGameplayStatistics);
        await store.SaveAsync(new TelemetryPreferences(true));

        Assert.True((await store.LoadAsync()).ShareAnonymousGameplayStatistics);
    }

    [Fact]
    public async Task OutboxPersistsOfflineAndAcknowledgesOnlyServerAcceptedMatches()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "outbox.json");
        var consent = new TelemetryConsent(true);
        var first = Summary(Guid.CreateVersion7());
        var second = Summary(Guid.CreateVersion7());
        using (var outbox = new TelemetryOutbox(path))
        {
            await outbox.EnqueueAsync(first, consent);
            await outbox.EnqueueAsync(second, consent);
        }

        using var reopened = new TelemetryOutbox(path);
        var transport = new FakeTransport(first.MatchId);
        var uploader = new TelemetryUploader(consent, reopened, transport);
        var complete = await uploader.UploadOnceAsync();

        Assert.False(complete);
        Assert.Equal(second.MatchId, Assert.Single(await reopened.PeekBatchAsync(25)).MatchId);
    }

    [Fact]
    public async Task OutboxIsBoundedAndDropsTheOldestItem()
    {
        using var temporary = new TemporaryDirectory();
        using var outbox = new TelemetryOutbox(Path.Combine(temporary.Path, "outbox.json"));
        var consent = new TelemetryConsent(true);
        var first = Summary(Guid.CreateVersion7());
        await outbox.EnqueueAsync(first, consent);
        for (var index = 1; index <= TelemetryOutbox.MaximumItems; index++)
        {
            await outbox.EnqueueAsync(Summary(Guid.CreateVersion7()), consent);
        }

        Assert.Equal(TelemetryOutbox.MaximumItems, await outbox.CountAsync());
        Assert.DoesNotContain(await outbox.PeekBatchAsync(25), item => item.MatchId == first.MatchId);
    }

    [Fact]
    public async Task DuplicateMatchIsStoredOnlyOnce()
    {
        using var temporary = new TemporaryDirectory();
        using var outbox = new TelemetryOutbox(Path.Combine(temporary.Path, "outbox.json"));
        var consent = new TelemetryConsent(true);
        var summary = Summary(Guid.CreateVersion7());

        await outbox.EnqueueAsync(summary, consent);
        await outbox.EnqueueAsync(summary, consent);

        Assert.Equal(1, await outbox.CountAsync());
    }

    [Fact]
    public async Task InvalidSchemaAndOversizedFieldsAreRejected()
    {
        using var temporary = new TemporaryDirectory();
        using var outbox = new TelemetryOutbox(Path.Combine(temporary.Path, "outbox.json"));
        var consent = new TelemetryConsent(true);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            outbox.EnqueueAsync(Summary(Guid.CreateVersion7()) with { TelemetrySchemaVersion = 99 }, consent));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            outbox.EnqueueAsync(Summary(Guid.CreateVersion7()) with { ClientVersion = new string('x', 65) }, consent));
        Assert.Equal(0, await outbox.CountAsync());
    }

    [Fact]
    public async Task FailedUploadKeepsBatchForLaterRetry()
    {
        using var temporary = new TemporaryDirectory();
        using var outbox = new TelemetryOutbox(Path.Combine(temporary.Path, "outbox.json"));
        var consent = new TelemetryConsent(true);
        var summary = Summary(Guid.CreateVersion7());
        await outbox.EnqueueAsync(summary, consent);
        var transport = new FlakyTransport();
        var uploader = new TelemetryUploader(consent, outbox, transport);

        await Assert.ThrowsAsync<HttpRequestException>(() => uploader.UploadOnceAsync());
        Assert.Equal(1, await outbox.CountAsync());
        Assert.True(await uploader.UploadOnceAsync());
        Assert.Equal(0, await outbox.CountAsync());
    }

    [Fact]
    public async Task RevokedConsentPreventsTransportCall()
    {
        using var temporary = new TemporaryDirectory();
        using var outbox = new TelemetryOutbox(Path.Combine(temporary.Path, "outbox.json"));
        var consent = new TelemetryConsent(true);
        await outbox.EnqueueAsync(Summary(Guid.CreateVersion7()), consent);
        consent.SetEnabled(false);
        var transport = new CountingTransport();

        Assert.False(await new TelemetryUploader(consent, outbox, transport).UploadOnceAsync());
        Assert.Equal(0, transport.Calls);
        Assert.Equal(1, await outbox.CountAsync());
    }

    [Fact]
    public void EndedTrackingSnapshotProducesOnlyReliableDerivedFields()
    {
        var lobby = LobbyState.Empty.SetPlayer(LobbyPlayer.Create(1) with
        {
            HeroCardId = "TB_BaconShop_HERO_41",
            TavernTier = 6,
            Triples = 2,
        });
        var snapshot = new TrackingSnapshot(
            4,
            TrackingSessionState.Ended,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMinutes(20),
            0, 0, 0, 0, 0, 0, 0,
            new BattlegroundsState(true, 12, BattlegroundsPhase.GameOver, 1, null, lobby),
            OpponentMemory.Empty,
            LobbyTimelineSnapshot.Empty);

        var summary = MatchSummaryFactory.Create(snapshot, "0.1.0");

        Assert.NotNull(summary);
        Assert.Equal("TB_BaconShop_HERO_41", summary.HeroCardId);
        Assert.Equal(12, summary.Turns);
        Assert.Equal(2, summary.Triples);
        Assert.Null(summary.Placement);
        Assert.NotEqual(Guid.Empty, summary.MatchId);
    }

    private static MatchSummary Summary(Guid matchId) => new(
        1,
        matchId,
        "battlegrounds",
        "unknown",
        null,
        "0.1.0",
        "HERO_TEST",
        null,
        null,
        10,
        Array.Empty<TavernProgressionEntry>(),
        1,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch.AddMinutes(10));

    private sealed class FakeTransport(Guid acknowledged) : ITelemetryTransport
    {
        public Task<TelemetryUploadResult> UploadAsync(
            IReadOnlyList<MatchSummary> summaries,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(2, summaries.Count);
            return Task.FromResult<TelemetryUploadResult>(new(new[] { acknowledged }));
        }
    }

    private sealed class FlakyTransport : ITelemetryTransport
    {
        private int _calls;

        public Task<TelemetryUploadResult> UploadAsync(
            IReadOnlyList<MatchSummary> summaries,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _calls) == 1)
            {
                throw new HttpRequestException("offline");
            }

            return Task.FromResult(new TelemetryUploadResult(summaries.Select(summary => summary.MatchId).ToArray()));
        }
    }

    private sealed class CountingTransport : ITelemetryTransport
    {
        public int Calls { get; private set; }

        public Task<TelemetryUploadResult> UploadAsync(
            IReadOnlyList<MatchSummary> summaries,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new TelemetryUploadResult(summaries.Select(summary => summary.MatchId).ToArray()));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IceCrowTelemetryTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
