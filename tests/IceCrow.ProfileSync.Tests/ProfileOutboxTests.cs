using System.Text.Json;
using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Tests;

public sealed class ProfileOutboxTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "icecrow-profile-outbox-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task EnqueueIsIdempotentByEventIdAndSurvivesReload()
    {
        var id = Guid.CreateVersion7();
        using (var outbox = Create())
        {
            Assert.Equal(ProfileOutboxResult.Enqueued, await outbox.EnqueueAsync(Match(id)));
            Assert.Equal(ProfileOutboxResult.Duplicate, await outbox.EnqueueAsync(Match(id)));
        }

        using var reopened = Create();
        var batch = await reopened.PeekBatchAsync(10);
        var single = Assert.Single(batch);
        Assert.Equal(id, single.EventId);
        Assert.Equal(ProfileEventType.BattlegroundsMatch, single.Type);
        Assert.Equal("exact", single.Payload.GetProperty("placementConfidence").GetString());
    }

    [Fact]
    public async Task LatestCollectionSnapshotReplacesThePendingOne()
    {
        using var outbox = Create();
        var older = ProfileEvent.Create(ProfileEventType.CollectionSnapshot, Timestamp, Collection("aaa"));
        var newer = ProfileEvent.Create(ProfileEventType.CollectionSnapshot, Timestamp.AddMinutes(1), Collection("bbb"));
        var match = Match(Guid.CreateVersion7());

        Assert.Equal(ProfileOutboxResult.Enqueued, await outbox.EnqueueAsync(older));
        Assert.Equal(ProfileOutboxResult.Enqueued, await outbox.EnqueueAsync(match));
        Assert.Equal(ProfileOutboxResult.Replaced, await outbox.EnqueueAsync(newer));

        var pending = await outbox.PeekBatchAsync(10);
        Assert.Equal([match.EventId, newer.EventId], pending.Select(static item => item.EventId));
    }

    [Fact]
    public async Task FullHistoryIsAnExplicitRejectionNotASilentDrop()
    {
        using var outbox = Create();
        for (var index = 0; index < ProfileOutbox.MaximumHistoryItems; index++)
        {
            Assert.Equal(ProfileOutboxResult.Enqueued, await outbox.EnqueueAsync(Match(Guid.CreateVersion7())));
        }

        Assert.Equal(ProfileOutboxResult.Full, await outbox.EnqueueAsync(Match(Guid.CreateVersion7())));
        Assert.Equal(ProfileOutboxResult.Enqueued, await outbox.EnqueueAsync(
            ProfileEvent.Create(ProfileEventType.CollectionSnapshot, Timestamp, Collection("still-fits"))));
        Assert.Equal(ProfileOutbox.MaximumHistoryItems + 1, await outbox.CountAsync());
    }

    [Fact]
    public async Task RemoveOnlyAffectsListedIds()
    {
        using var outbox = Create();
        var first = Match(Guid.CreateVersion7());
        var second = Match(Guid.CreateVersion7());
        await outbox.EnqueueAsync(first);
        await outbox.EnqueueAsync(second);

        var removed = await outbox.RemoveAsync([first.EventId, Guid.CreateVersion7()]);

        Assert.Equal(1, removed);
        var remaining = Assert.Single(await outbox.PeekBatchAsync(10));
        Assert.Equal(second.EventId, remaining.EventId);
    }

    [Fact]
    public async Task CorruptOrForeignFileIsInvalidData()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "outbox.json"), "[{\"eventId\":\"not-a-guid\"}]");
        using var outbox = Create();

        await Assert.ThrowsAsync<InvalidDataException>(() => outbox.CountAsync());
    }

    [Fact]
    public void OversizedPayloadsAndUnknownTypesAreRejectedAtCreation()
    {
        var huge = new CollectionSnapshotRecord(
            Timestamp,
            "hash",
            Enumerable.Range(0, 30_000).Select(index => new CollectionCardRecord($"CARD_{index:D6}", 1, 0, null, null)).ToArray());

        Assert.Throws<InvalidDataException>(() => ProfileEvent.Create(ProfileEventType.CollectionSnapshot, Timestamp, huge));
        Assert.Throws<ArgumentException>(() => ProfileEvent.Create("future_event", Timestamp, Collection("x")));
        Assert.Throws<InvalidDataException>(() => ProfileEvent.Validate(
            new ProfileEvent(Guid.Empty, ProfileEventType.ArenaRun, 1, Timestamp, JsonDocument.Parse("{}").RootElement)));
    }

    private ProfileOutbox Create() => new(Path.Combine(_directory, "outbox.json"));

    private static ProfileEvent Match(Guid eventId) => ProfileEvent.Create(
        ProfileEventType.BattlegroundsMatch,
        Timestamp,
        new BattlegroundsMatchRecord(
            Guid.CreateVersion7(),
            BattlegroundsMode.Solo,
            null,
            null,
            Certainty.Unknown,
            "TB_BaconShop_HERO_41",
            3,
            Certainty.Exact,
            Timestamp,
            Timestamp.AddMinutes(20),
            1200,
            14,
            null,
            224857),
        eventId);

    private static CollectionSnapshotRecord Collection(string hash) =>
        new(Timestamp, hash, [new CollectionCardRecord("CS2_029", 2, 0, null, null)]);
}
