using System.Security.Cryptography;
using System.Text;
using IceCrow.Hearthstone.ClientState;
using IceCrow.ProfileSync.Collection;
using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Tests;

public sealed class CollectionSyncCoordinatorTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "icecrow-collection-sync-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileOutbox _outbox;

    public CollectionSyncCoordinatorTests()
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
    public void HashUsesTheDocumentedCanonicalLines()
    {
        var cards = CollectionContentHash.Normalize(Snapshot(Timestamp, new CollectionCard("CS2_029", 2, 0, null, 1)));
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("CS2_029|2|0|-|1\n")));

        Assert.Equal(expected, CollectionContentHash.Compute(cards));
        Assert.Equal(64, expected.Length);
    }

    [Fact]
    public void HashIsStableAcrossReorderingAndDuplicates()
    {
        var ordered = Snapshot(Timestamp, new CollectionCard("A", 1, 0, null, null), new CollectionCard("B", 2, 1, 1, null));
        var reordered = Snapshot(Timestamp.AddDays(1), new CollectionCard("B", 2, 1, 1, null), new CollectionCard("A", 1, 0, null, null));
        var duplicated = Snapshot(Timestamp, new CollectionCard("B", 1, 1, null, null), new CollectionCard("A", 1, 0, null, null), new CollectionCard("B", 1, 0, 1, null));
        var different = Snapshot(Timestamp, new CollectionCard("A", 1, 0, null, null), new CollectionCard("B", 2, 1, null, null));

        var hash = CollectionContentHash.Compute(CollectionContentHash.Normalize(ordered));

        Assert.Equal(hash, CollectionContentHash.Compute(CollectionContentHash.Normalize(reordered)));
        Assert.Equal(hash, CollectionContentHash.Compute(CollectionContentHash.Normalize(duplicated)));
        Assert.NotEqual(hash, CollectionContentHash.Compute(CollectionContentHash.Normalize(different)));
    }

    [Fact]
    public void ZeroCountEntriesAreDroppedAndCardsAreSortedOrdinally()
    {
        var cards = CollectionContentHash.Normalize(Snapshot(
            Timestamp,
            new CollectionCard("b", 1, 0, null, null),
            new CollectionCard("EMPTY", 0, 0, 0, null),
            new CollectionCard("B", 0, 0, null, 1),
            new CollectionCard("A", 0, 1, null, null)));

        Assert.Equal(["A", "B", "b"], cards.Select(static card => card.CardId));
    }

    [Fact]
    public async Task UnchangedCollectionIsANoOpWithoutOutboxWrite()
    {
        var source = new ScriptedCollectionSource(
            () => Snapshot(Timestamp, new CollectionCard("A", 1, 0, null, null)),
            () => Snapshot(Timestamp.AddHours(1), new CollectionCard("A", 1, 0, null, null)));
        using var coordinator = Create(source);

        Assert.Equal(CollectionSyncOutcome.Enqueued, await coordinator.RefreshAsync(CollectionRefreshTrigger.Startup));
        var pending = Assert.Single(await _outbox.PeekBatchAsync(10));
        Assert.Equal(CollectionSyncOutcome.Unchanged, await coordinator.RefreshAsync(CollectionRefreshTrigger.PackOpeningExit));

        var still = Assert.Single(await _outbox.PeekBatchAsync(10));
        Assert.Equal(pending.EventId, still.EventId);
        Assert.Equal(CollectionRefreshTrigger.PackOpeningExit, coordinator.Status.LastTrigger);
        Assert.Equal(CollectionSyncOutcome.Unchanged, coordinator.Status.LastOutcome);
        Assert.Equal(Timestamp, coordinator.Status.LastObservedAt);
    }

    [Fact]
    public async Task ChangedCollectionIsEnqueuedThenReplaced()
    {
        var source = new ScriptedCollectionSource(
            () => Snapshot(Timestamp, new CollectionCard("A", 1, 0, null, null)),
            () => Snapshot(Timestamp.AddHours(1), new CollectionCard("A", 2, 0, null, null)));
        using var coordinator = Create(source);

        Assert.Equal(CollectionSyncOutcome.Enqueued, await coordinator.RefreshAsync(CollectionRefreshTrigger.Startup));
        Assert.Equal(CollectionSyncOutcome.Replaced, await coordinator.RefreshAsync(CollectionRefreshTrigger.CollectionManagerExit));

        var pending = Assert.Single(await _outbox.PeekBatchAsync(10));
        Assert.Equal(ProfileEventType.CollectionSnapshot, pending.Type);
        var payload = pending.Payload;
        Assert.Equal(coordinator.Status.LastHashPrefix, payload.GetProperty("contentHash").GetString()![..8]);
        Assert.Equal(2, payload.GetProperty("cards")[0].GetProperty("normalCount").GetInt32());
        Assert.Equal(1, coordinator.Status.CardCount);
        Assert.Equal(8, coordinator.Status.LastHashPrefix!.Length);
    }

    [Fact]
    public async Task ZeroCountEntriesNeverReachTheOutbox()
    {
        var source = new ScriptedCollectionSource(() => Snapshot(
            Timestamp,
            new CollectionCard("OWNED", 1, 0, null, null),
            new CollectionCard("NOT_OWNED", 0, 0, null, 0)));
        using var coordinator = Create(source);

        Assert.Equal(CollectionSyncOutcome.Enqueued, await coordinator.RefreshAsync(CollectionRefreshTrigger.Manual));

        var pending = Assert.Single(await _outbox.PeekBatchAsync(10));
        var cards = pending.Payload.GetProperty("cards");
        Assert.Equal(1, cards.GetArrayLength());
        Assert.Equal("OWNED", cards[0].GetProperty("cardId").GetString());
    }

    [Fact]
    public async Task UnavailableOrFailingSourceWritesNothing()
    {
        var source = new ScriptedCollectionSource(
            () => null,
            () => throw new IOException("simulated client failure"));
        using var coordinator = Create(source);

        Assert.Equal(CollectionSyncOutcome.Unavailable, await coordinator.RefreshAsync(CollectionRefreshTrigger.Startup));
        Assert.Equal(CollectionSyncOutcome.Unavailable, await coordinator.RefreshAsync(CollectionRefreshTrigger.Manual));

        Assert.Equal(0, await _outbox.CountAsync());
        Assert.False(File.Exists(StatePath));
        Assert.Equal(1, coordinator.Status.SourceFailures);
        Assert.Null(coordinator.Status.LastHashPrefix);
    }

    [Fact]
    public async Task StateFileSurvivesReloadSoARestartDoesNotResend()
    {
        CollectionSnapshot? Read() => Snapshot(Timestamp, new CollectionCard("A", 1, 0, null, null));
        string prefix;
        using (var first = Create(new ScriptedCollectionSource(Read)))
        {
            Assert.Equal(CollectionSyncOutcome.Enqueued, await first.RefreshAsync(CollectionRefreshTrigger.Startup));
            prefix = first.Status.LastHashPrefix!;
        }

        Assert.True(new FileInfo(StatePath).Length <= CollectionSyncStateFile.MaximumFileBytes);
        using var second = Create(new ScriptedCollectionSource(Read));

        Assert.Equal(CollectionSyncOutcome.Unchanged, await second.RefreshAsync(CollectionRefreshTrigger.Startup));
        Assert.Equal(prefix, second.Status.LastHashPrefix);
        Assert.Equal(1, second.Status.CardCount);
        Assert.Equal(1, await _outbox.CountAsync());
    }

    [Fact]
    public async Task CorruptStateFileMeansNeverSyncedRatherThanAFailure()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(StatePath, "{\"contentHash\":");
        using var coordinator = Create(new ScriptedCollectionSource(() => Snapshot(Timestamp, new CollectionCard("A", 1, 0, null, null))));

        Assert.Equal(CollectionSyncOutcome.Enqueued, await coordinator.RefreshAsync(CollectionRefreshTrigger.Startup));
        Assert.NotNull(await new CollectionSyncStateFile(StatePath).LoadAsync());
    }

    [Fact]
    public async Task OversizedCollectionIsRejectedVisiblyNotTruncated()
    {
        var huge = Snapshot(
            Timestamp,
            Enumerable.Range(0, CollectionSnapshot.MaximumCards)
                .Select(index => new CollectionCard($"CARD_{index:D6}", 1, 0, null, null))
                .ToArray());
        var source = new ScriptedCollectionSource(
            () => huge,
            () => Snapshot(Timestamp.AddHours(1), new CollectionCard("A", 1, 0, null, null)));
        using var coordinator = Create(source);

        Assert.Equal(CollectionSyncOutcome.Rejected, await coordinator.RefreshAsync(CollectionRefreshTrigger.Startup));

        Assert.Equal(0, await _outbox.CountAsync());
        Assert.Equal(1, coordinator.Status.RejectedSnapshots);
        Assert.Null(coordinator.Status.LastHashPrefix);
        Assert.Equal(CollectionSyncOutcome.Enqueued, await coordinator.RefreshAsync(CollectionRefreshTrigger.Manual));
    }

    private string StatePath => Path.Combine(_directory, "collection-sync.json");

    private CollectionSyncCoordinator Create(ScriptedCollectionSource source) =>
        new(source, _outbox, StatePath);

    private static CollectionSnapshot Snapshot(DateTimeOffset observedAt, params CollectionCard[] cards) =>
        new(observedAt, cards);

    private sealed class ScriptedCollectionSource(params Func<CollectionSnapshot?>[] reads) : ICollectionSource
    {
        private int _index;

        public ClientStateProviderStatus Status => ClientStateProviderStatus.Connected;

        public ValueTask<CollectionSnapshot?> ReadAsync(CancellationToken cancellationToken = default)
        {
            var read = reads[Math.Min(_index, reads.Length - 1)];
            _index++;
            return ValueTask.FromResult(read());
        }
    }
}
