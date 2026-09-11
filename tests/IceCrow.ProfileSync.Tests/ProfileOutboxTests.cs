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
    public async Task EnqueueRejectsTheSameMatchWithDifferentGeneratedIds()
    {
        using var outbox = Create();
        var first = Match(Guid.CreateVersion7());
        var duplicate = Match(Guid.CreateVersion7());

        Assert.Equal(ProfileOutboxResult.Enqueued, await outbox.EnqueueAsync(first));
        Assert.Equal(ProfileOutboxResult.Duplicate, await outbox.EnqueueAsync(duplicate));
        Assert.Equal(first.EventId, Assert.Single(await outbox.PeekBatchAsync(10)).EventId);
    }

    [Fact]
    public async Task ReloadCompactsLegacyDuplicateMatchesBeforeUpload()
    {
        var first = Match(Guid.CreateVersion7());
        var duplicate = Match(Guid.CreateVersion7());
        Directory.CreateDirectory(_directory);
        var journalPath = Path.Combine(_directory, "outbox.jsonl");
        await File.WriteAllLinesAsync(
            journalPath,
            [JournalLine(first), JournalLine(duplicate)]);

        using var outbox = Create();

        Assert.Equal(1, await outbox.CountAsync());
        Assert.Equal(first.EventId, Assert.Single(await outbox.PeekBatchAsync(10)).EventId);
        Assert.Single(await File.ReadAllLinesAsync(journalPath));
    }

    [Fact]
    public void StableMatchIdentityDoesNotTurnOutOfOrderLogTimesIntoAnException()
    {
        var startedAt = Timestamp.AddMinutes(1);

        var first = ProfileMatchIdentity.CreateEventId(ProfileEventType.ConstructedMatch, startedAt, Timestamp);
        var replay = ProfileMatchIdentity.CreateEventId(ProfileEventType.ConstructedMatch, startedAt, Timestamp);

        Assert.Equal(first, replay);
        Assert.NotEqual(Guid.Empty, first);
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
        const int capacity = 4;
        using var outbox = new ProfileOutbox(Path.Combine(_directory, "outbox.json"), maximumHistoryItems: capacity);
        for (var index = 0; index < capacity; index++)
        {
            Assert.Equal(ProfileOutboxResult.Enqueued, await outbox.EnqueueAsync(Match(Guid.CreateVersion7(), index)));
        }

        Assert.Equal(ProfileOutboxResult.Full, await outbox.EnqueueAsync(Match(Guid.CreateVersion7(), capacity)));
        Assert.Equal(ProfileOutboxResult.Enqueued, await outbox.EnqueueAsync(
            ProfileEvent.Create(ProfileEventType.CollectionSnapshot, Timestamp, Collection("still-fits"))));
        Assert.Equal(capacity + 1, await outbox.CountAsync());
        Assert.Equal(4096, ProfileOutbox.MaximumHistoryItems);
    }

    [Fact]
    public async Task RemoveOnlyAffectsListedIds()
    {
        using var outbox = Create();
        var first = Match(Guid.CreateVersion7());
        var second = Match(Guid.CreateVersion7(), 1);
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
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "outbox.jsonl"),
            """
            {"event":{"eventId":"not-a-guid"}}
            {"ack":"x"}

            """);
        using var outbox = Create();

        await Assert.ThrowsAsync<InvalidDataException>(() => outbox.CountAsync());
    }

    [Fact]
    public void OversizedPayloadsAndUnknownTypesAreRejectedAtCreation()
    {
        var fullCollection = new CollectionSnapshotRecord(
            Timestamp,
            "hash",
            Enumerable.Range(0, 6_000).Select(index => new CollectionCardRecord($"CARD_{index:D6}", 2, 1, null, null)).ToArray());
        var huge = fullCollection with
        {
            Cards = Enumerable.Range(0, 30_000)
                .Select(index => new CollectionCardRecord(new string('x', 150) + index, 1, 0, null, null))
                .ToArray(),
        };
        var oversizedHistory = new ConstructedMatchRecord(
            Guid.CreateVersion7(), "ranked", "standard", MatchResult.Won, Certainty.Exact, Timestamp, Timestamp, 0, 0,
            null, null, DeckEvidence.Unknown, MulliganRecord.Unknown, null,
            new OpponentDeckEvidence(Enumerable.Range(0, 20_000).Select(static index => $"CARD_{index:D6}_padding_padding_padding").ToArray(), null, null, Certainty.Partial),
            null, null, null);

        var accepted = ProfileEvent.Create(ProfileEventType.CollectionSnapshot, Timestamp, fullCollection);
        Assert.DoesNotContain("signatureCount", accepted.Payload.GetRawText(), StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => ProfileEvent.Create(ProfileEventType.CollectionSnapshot, Timestamp, huge));
        Assert.Throws<InvalidDataException>(() => ProfileEvent.Create(ProfileEventType.ConstructedMatch, Timestamp, oversizedHistory));
        Assert.Throws<ArgumentException>(() => ProfileEvent.Create("future_event", Timestamp, Collection("x")));

        var tooManyMulliganCards = oversizedHistory with
        {
            OpponentDeck = OpponentDeckEvidence.Unknown,
            PlayerMulligan = new MulliganRecord(
                Enumerable.Range(0, ProfileRecordLimits.MaximumMulliganCards + 1).Select(static index => $"CARD_{index}").ToArray(),
                [], [], [], Certainty.Exact),
        };
        Assert.Throws<InvalidDataException>(() => ProfileEvent.Create(ProfileEventType.ConstructedMatch, Timestamp, tooManyMulliganCards));
        var eightMinions = new BattlegroundsMatchRecord(
            Guid.CreateVersion7(), BattlegroundsMode.Solo, null, null, Certainty.Unknown, null, null, Certainty.Unknown,
            Timestamp, Timestamp, 0, 0,
            new FinalBoardRecord(Timestamp, 1, Enumerable.Range(1, 8).Select(static slot => new FinalBoardMinion(slot, "M", 1, 1, null)).ToArray(), Certainty.Exact),
            null);
        Assert.Throws<InvalidDataException>(() => ProfileEvent.Create(ProfileEventType.BattlegroundsMatch, Timestamp, eightMinions));
        Assert.Throws<InvalidDataException>(() => ProfileEvent.Validate(
            new ProfileEvent(Guid.Empty, ProfileEventType.ArenaRun, 1, Timestamp, JsonDocument.Parse("{}").RootElement)));
    }

    private ProfileOutbox Create() => new(Path.Combine(_directory, "outbox.json"));

    private static ProfileEvent Match(Guid eventId, int startOffsetMinutes = 0)
    {
        var startedAt = Timestamp.AddMinutes(startOffsetMinutes);
        return ProfileEvent.Create(
        ProfileEventType.BattlegroundsMatch,
        startedAt,
        new BattlegroundsMatchRecord(
            Guid.CreateVersion7(),
            BattlegroundsMode.Solo,
            null,
            null,
            Certainty.Unknown,
            "TB_BaconShop_HERO_41",
            3,
            Certainty.Exact,
            startedAt,
            startedAt.AddMinutes(20),
            1200,
            14,
            null,
            224857),
        eventId);
    }

    private static CollectionSnapshotRecord Collection(string hash) =>
        new(Timestamp, hash, [new CollectionCardRecord("CS2_029", 2, 0, null, null)]);

    private static string JournalLine(ProfileEvent profileEvent) =>
        JsonSerializer.Serialize(new { Event = profileEvent }, ProfileJson.Options);
}
