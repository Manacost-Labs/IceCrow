using IceCrow.ProfileSync.History;
using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Tests;

public sealed class ProfileHistoryStoreTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "icecrow-history-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task UploadedOutboxRemovalDoesNotRemoveLocalMatchHistory()
    {
        var profileEvent = Constructed(format: "standard", result: MatchResult.Won);
        using (var history = History())
        using (var outbox = new ProfileOutbox(Path.Combine(_directory, "outbox", "outbox.json")))
        {
            Assert.Equal(ProfileHistoryAppendResult.Added, await history.AppendAsync(profileEvent));
            Assert.Equal(ProfileOutboxResult.Enqueued, await outbox.EnqueueAsync(profileEvent));
            Assert.Equal(1, await outbox.RemoveAsync([profileEvent.EventId]));
            Assert.Equal(0, await outbox.CountAsync());
        }

        using var reopened = History();
        var snapshot = await reopened.ReadAsync();
        var match = Assert.Single(snapshot.Matches);
        Assert.Equal(profileEvent.EventId, match.EventId);
        Assert.Equal(HistoryGameMode.Standard, match.Mode);
    }

    [Fact]
    public async Task DuplicateEventIsIdempotentAcrossRestart()
    {
        var profileEvent = Constructed();
        using (var first = History())
        {
            Assert.Equal(ProfileHistoryAppendResult.Added, await first.AppendAsync(profileEvent));
        }

        using var reopened = History();
        Assert.Equal(ProfileHistoryAppendResult.Duplicate, await reopened.AppendAsync(profileEvent));
        Assert.Single((await reopened.ReadAsync()).Matches);
        Assert.Single(await File.ReadAllLinesAsync(HistoryPath));
    }

    [Fact]
    public async Task TruncatedFinalLineIsRecoveredAndRewrittenBeforeNextAppend()
    {
        var first = Constructed(eventId: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var second = Arena(eventId: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        using (var history = History())
        {
            Assert.Equal(ProfileHistoryAppendResult.Added, await history.AppendAsync(first));
        }

        await File.AppendAllTextAsync(HistoryPath, "{\"eventId\":");
        using (var recovered = History())
        {
            var snapshot = await recovered.ReadAsync();
            Assert.True(snapshot.RecoveredTruncatedTail);
            Assert.Single(snapshot.Matches);
            Assert.Equal(ProfileHistoryAppendResult.Added, await recovered.AppendAsync(second));
            Assert.False((await recovered.ReadAsync()).RecoveredTruncatedTail);
        }

        using var reopened = History();
        var final = await reopened.ReadAsync();
        Assert.False(final.RecoveredTruncatedTail);
        Assert.Equal(2, final.Matches.Length);
    }

    [Fact]
    public async Task InteriorCorruptionIsRejectedWithoutReturningPartialHistory()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(HistoryPath, "not-json\nnot-a-tail\n");
        using var history = History();

        await Assert.ThrowsAsync<InvalidDataException>(() => history.ReadAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => history.ReadAsync());
    }

    [Fact]
    public async Task CapacityIsExplicitAndPreservesTheOldestAcceptedMatch()
    {
        var first = Constructed(eventId: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var second = Constructed(eventId: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        using var history = History(maximumItems: 1);

        Assert.Equal(ProfileHistoryAppendResult.Added, await history.AppendAsync(first));
        Assert.Equal(ProfileHistoryAppendResult.Full, await history.AppendAsync(second));
        Assert.Equal(first.EventId, Assert.Single((await history.ReadAsync()).Matches).EventId);
    }

    [Fact]
    public async Task CollectionStateIsNotWrittenIntoMatchHistory()
    {
        var collection = ProfileEvent.Create(
            ProfileEventType.CollectionSnapshot,
            Timestamp,
            new CollectionSnapshotRecord(Timestamp, "sha256:empty", []));
        using var history = History();

        Assert.Equal(ProfileHistoryAppendResult.Ignored, await history.AppendAsync(collection));
        var snapshot = await history.ReadAsync();
        Assert.Empty(snapshot.Matches);
        Assert.Empty(snapshot.Decks);
        Assert.Equal(0, snapshot.SourceEvents);
        Assert.False(File.Exists(HistoryPath));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ProfileHistoryStore.MaximumItems + 1)]
    public void CapacityOutsideSupportedBoundsIsRejected(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => History(capacity));
        Assert.False(File.Exists(HistoryPath));
    }

    [Fact]
    public async Task OversizedHistoryIsRejectedBeforeAnyEventIsMaterialized()
    {
        Directory.CreateDirectory(_directory);
        await using (var stream = new FileStream(HistoryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(ProfileHistoryStore.MaximumFileBytes + 1L);
        }

        using var history = History();
        await Assert.ThrowsAsync<InvalidDataException>(() => history.ReadAsync());
    }

    private string HistoryPath => Path.Combine(_directory, "matches.jsonl");

    private ProfileHistoryStore History(int maximumItems = ProfileHistoryStore.MaximumItems) =>
        new(HistoryPath, maximumItems);

    private static ProfileEvent Constructed(
        string format = "wild",
        MatchResult result = MatchResult.Lost,
        Guid? eventId = null,
        DeckEvidence? deck = null) =>
        ProfileEvent.Create(
            ProfileEventType.ConstructedMatch,
            Timestamp,
            new ConstructedMatchRecord(
                Guid.CreateVersion7(),
                "ranked",
                format,
                result,
                Certainty.Exact,
                Timestamp,
                Timestamp.AddMinutes(8),
                480,
                10,
                "HERO_01",
                "HERO_02",
                deck ?? DeckEvidence.Unknown,
                MulliganRecord.Unknown,
                null,
                OpponentDeckEvidence.Unknown,
                null,
                224857,
                2),
            eventId);

    private static ProfileEvent Arena(Guid? eventId = null) =>
        ProfileEvent.Create(
            ProfileEventType.ArenaMatch,
            Timestamp.AddMinutes(1),
            new ArenaMatchRecord(
                Guid.CreateVersion7(),
                null,
                null,
                null,
                Certainty.Unknown,
                MatchResult.Won,
                Certainty.Exact,
                "HERO_03",
                "HERO_04",
                Timestamp,
                Timestamp.AddMinutes(9),
                540,
                11,
                MulliganRecord.Unknown,
                null,
                224857),
            eventId);
}
