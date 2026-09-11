using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Tests;

/// <summary>
/// The outbox journal: one appended line per event, acknowledgement
/// tombstones, compaction, crash-tail recovery, legacy import, and the
/// latest-only collection file.
/// </summary>
public sealed class ProfileJournalTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "icecrow-profile-journal-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task EnqueueAppendsOneLineWithoutRewritingTheJournal()
    {
        using var outbox = Create();
        await outbox.EnqueueAsync(Match(0));
        var afterFirst = new FileInfo(JournalPath).Length;

        await outbox.EnqueueAsync(Match(1));
        var afterSecond = new FileInfo(JournalPath).Length;

        Assert.Equal(2, File.ReadAllLines(JournalPath).Length);
        Assert.InRange(afterSecond - afterFirst, afterFirst - 64, afterFirst + 64);
        Assert.False(File.Exists(Path.Combine(_directory, "collection-pending.json")));
    }

    [Fact]
    public async Task ReplayCanReadWhileTheJournalWriterIsOpen()
    {
        using (var outbox = Create())
        {
            await outbox.EnqueueAsync(Match());
        }

        await using var writer = new FileStream(
            JournalPath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous);
        using var reader = Create();

        Assert.Equal(1, await reader.CountAsync());
    }

    [Fact]
    public async Task AcknowledgementsAreTombstonesUntilCompaction()
    {
        using var outbox = Create();
        var ids = new List<Guid>();
        for (var index = 0; index < 4; index++)
        {
            var match = Match(index);
            ids.Add(match.EventId);
            await outbox.EnqueueAsync(match);
        }

        Assert.Equal(2, await outbox.RemoveAsync(ids.Take(2)));

        var lines = File.ReadAllLines(JournalPath);
        Assert.Equal(6, lines.Length);
        Assert.Equal(2, lines.Count(static line => line.Contains("\"ack\"", StringComparison.Ordinal)));
        Assert.Equal(2, await outbox.CountAsync());

        using var reopened = Create();
        Assert.Equal(ids.Skip(2), (await reopened.PeekBatchAsync(10)).Select(static item => item.EventId));
    }

    [Fact]
    public async Task CompactionRewritesOnlyLiveEventsAfterEnoughTombstones()
    {
        using var outbox = Create();
        var ids = new List<Guid>();
        for (var index = 0; index < ProfileOutbox.CompactionTombstones + 1; index++)
        {
            var match = Match(index);
            ids.Add(match.EventId);
            await outbox.EnqueueAsync(match);
        }

        foreach (var batch in ids
                     .Take(ProfileOutbox.CompactionTombstones)
                     .Chunk(ProfileOutbox.MaximumBatchSize * 2))
        {
            await outbox.RemoveAsync(batch);
        }

        var lines = File.ReadAllLines(JournalPath);
        Assert.Single(lines);
        Assert.DoesNotContain("\"ack\"", lines[0], StringComparison.Ordinal);
        Assert.Equal(ids[^1], Assert.Single(await outbox.PeekBatchAsync(10)).EventId);
    }

    [Fact]
    public async Task CrashTruncatedTailIsRecoveredAndCounted()
    {
        Guid survivor;
        using (var outbox = Create())
        {
            var match = Match();
            survivor = match.EventId;
            await outbox.EnqueueAsync(match);
        }

        var partial = File.ReadAllText(JournalPath);
        await File.WriteAllTextAsync(JournalPath, partial + "{\"event\":{\"eventId\":\"0198a0b1-1234-7000-8000-");

        using var reopened = Create();
        Assert.Equal(1, await reopened.CountAsync());
        Assert.True(reopened.TruncatedTailRecovered);
        Assert.Equal(survivor, Assert.Single(await reopened.PeekBatchAsync(10)).EventId);
        Assert.Single(File.ReadAllLines(JournalPath));
    }

    [Fact]
    public async Task CorruptMiddleLineIsInvalidDataNotSilentlySkipped()
    {
        using (var outbox = Create())
        {
            await outbox.EnqueueAsync(Match(0));
            await outbox.EnqueueAsync(Match(1));
        }

        var lines = File.ReadAllLines(JournalPath);
        lines[0] = "{\"event\":{\"eventId\":\"not-a-guid\"}}";
        await File.WriteAllLinesAsync(JournalPath, lines);

        using var reopened = Create();
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.CountAsync());
    }

    [Fact]
    public async Task InvalidUtf8IsReportedAsInvalidData()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(JournalPath, [0xff, (byte)'\n']);
        using var reopened = Create();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => reopened.CountAsync());

        Assert.IsType<System.Text.DecoderFallbackException>(exception.InnerException);
    }

    [Fact]
    public async Task LegacySingleFileOutboxIsImportedOnce()
    {
        Directory.CreateDirectory(_directory);
        var legacy = Match();
        var legacyCollection = ProfileEvent.Create(ProfileEventType.CollectionSnapshot, Timestamp, Collection("legacy"));
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "outbox.json"),
            System.Text.Json.JsonSerializer.Serialize(new[] { legacy, legacyCollection }, ProfileJson.Options));

        using var outbox = Create();

        Assert.Equal(2, await outbox.CountAsync());
        Assert.False(File.Exists(Path.Combine(_directory, "outbox.json")));
        Assert.True(File.Exists(Path.Combine(_directory, "outbox.json.migrated")));
        var batch = await outbox.PeekBatchAsync(10);
        Assert.Equal([legacy.EventId, legacyCollection.EventId], batch.Select(static item => item.EventId));
    }

    [Fact]
    public async Task CollectionLivesInItsOwnFileAndIsReplacedAtomically()
    {
        using var outbox = Create();
        var first = ProfileEvent.Create(ProfileEventType.CollectionSnapshot, Timestamp, Collection("a"));
        var second = ProfileEvent.Create(ProfileEventType.CollectionSnapshot, Timestamp.AddMinutes(1), Collection("b"));

        Assert.Equal(ProfileOutboxResult.Enqueued, await outbox.EnqueueAsync(first));
        Assert.Equal(ProfileOutboxResult.Replaced, await outbox.EnqueueAsync(second));

        Assert.False(File.Exists(JournalPath));
        Assert.True(File.Exists(Path.Combine(_directory, "collection-pending.json")));
        Assert.Equal(second.EventId, Assert.Single(await outbox.PeekBatchAsync(10)).EventId);
        Assert.Equal(1, await outbox.RemoveAsync([second.EventId]));
        Assert.False(File.Exists(Path.Combine(_directory, "collection-pending.json")));
    }

    [Fact]
    public async Task HistoryCapacityIsExplicitAndSurvivesRestart()
    {
        using (var outbox = new ProfileOutbox(Path.Combine(_directory, "outbox.json"), maximumHistoryItems: 3))
        {
            for (var index = 0; index < 3; index++)
            {
                Assert.Equal(ProfileOutboxResult.Enqueued, await outbox.EnqueueAsync(Match(index)));
            }

            Assert.Equal(ProfileOutboxResult.Full, await outbox.EnqueueAsync(Match(3)));
        }

        using var reopened = new ProfileOutbox(Path.Combine(_directory, "outbox.json"), maximumHistoryItems: 3);
        Assert.Equal(3, await reopened.CountAsync());
        Assert.Equal(ProfileOutboxResult.Full, await reopened.EnqueueAsync(Match(4)));
    }

    [Fact]
    public async Task ReplayRejectsAJournalAboveTheConfiguredEventBudget()
    {
        using (var writer = Create())
        {
            for (var index = 0; index < 4; index++)
            {
                Assert.Equal(ProfileOutboxResult.Enqueued, await writer.EnqueueAsync(Match(index)));
            }
        }

        using var bounded = new ProfileOutbox(
            Path.Combine(_directory, "outbox.json"),
            maximumHistoryItems: 3);

        await Assert.ThrowsAsync<InvalidDataException>(() => bounded.CountAsync());
    }

    private string JournalPath => Path.Combine(_directory, "outbox.jsonl");

    private ProfileOutbox Create() => new(Path.Combine(_directory, "outbox.json"));

    private static ProfileEvent Match(int startOffsetMinutes = 0)
    {
        var startedAt = Timestamp.AddMinutes(startOffsetMinutes);
        return ProfileEvent.Create(
        ProfileEventType.ConstructedMatch,
        startedAt,
        new ConstructedMatchRecord(
            Guid.CreateVersion7(), "ranked", "standard", MatchResult.Won, Certainty.Exact,
            startedAt, startedAt.AddMinutes(7), 420, 10, "HERO_01", "HERO_02",
            DeckEvidence.Unknown, MulliganRecord.Unknown, null, OpponentDeckEvidence.Unknown, null, 224857, 2));
    }

    private static CollectionSnapshotRecord Collection(string hash) =>
        new(Timestamp, hash, [new CollectionCardRecord("CS2_029", 2, 0, null, null)]);
}
