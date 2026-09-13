using IceCrow.ProfileSync.History;
using IceCrow.ProfileSync.History.Decks;
using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Tests;

public sealed class DeckLibraryTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "IceCrow.DeckLibraryTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RegisteredSelectionBecomesNamedActiveFamilyAndPersists()
    {
        var path = CatalogPath();
        var history = History(Standard("DECK_A", MatchResult.Won, Timestamp.AddMinutes(5)));
        using (var library = new DeckLibrary(path))
        {
            await library.InitializeAsync();
            library.ApplyHistory(history);

            var result = await library.RegisterSelectionAsync(
                "Контроль воин",
                "standard",
                "DECK_A",
                "HERO_01");

            Assert.True(result.Success);
            var family = Assert.Single(result.Snapshot.Families);
            Assert.Equal("Контроль воин", family.Name);
            Assert.True(family.IsActive);
            Assert.Equal(1, family.Games);
            Assert.Equal(1, family.Wins);
            Assert.Equal("HERO_01", family.HeroCardId);
        }

        using var reopened = new DeckLibrary(path);
        await reopened.InitializeAsync();
        reopened.ApplyHistory(history);
        var restored = Assert.Single(reopened.Snapshot.Families);
        Assert.Equal("Контроль воин", restored.Name);
        Assert.True(restored.IsActive);
    }

    [Fact]
    public async Task MergeAggregatesVersionsWithoutRewritingMatchHistory()
    {
        var history = History(
            Standard("DECK_A", MatchResult.Won, Timestamp.AddMinutes(5)),
            Standard("DECK_B", MatchResult.Lost, Timestamp.AddMinutes(10)));
        using var library = new DeckLibrary(CatalogPath());
        await library.InitializeAsync();
        library.ApplyHistory(history);
        var originalFamilies = library.Snapshot.Families;

        var result = await library.MergeAsync(originalFamilies[0].Id, originalFamilies[1].Id);

        Assert.True(result.Success);
        var merged = Assert.Single(result.Snapshot.Families);
        Assert.Equal(2, merged.Versions.Length);
        Assert.Equal(2, merged.Games);
        Assert.Equal(1, merged.Wins);
        Assert.Equal(1, merged.Losses);
        Assert.Equal("DECK_B", merged.CurrentVersion.Identity.DeckCode);
        Assert.Equal(["DECK_B", "DECK_A"], history.Matches.Select(static match => match.DeckCode));
        Assert.Equal(2, history.Decks.Length);
    }

    [Fact]
    public async Task SeparateMakesEveryExactRevisionIndependentAgain()
    {
        var history = History(
            Standard("DECK_A", MatchResult.Won, Timestamp.AddMinutes(5)),
            Standard("DECK_B", MatchResult.Lost, Timestamp.AddMinutes(10)));
        using var library = new DeckLibrary(CatalogPath());
        await library.InitializeAsync();
        library.ApplyHistory(history);
        var original = library.Snapshot.Families;
        var merged = await library.MergeAsync(original[0].Id, original[1].Id);

        var separated = await library.SeparateAsync(Assert.Single(merged.Snapshot.Families).Id);

        Assert.True(separated.Success);
        Assert.Equal(2, separated.Snapshot.Families.Length);
        Assert.All(separated.Snapshot.Families, static family => Assert.Single(family.Versions));
        Assert.Equal(2, separated.Snapshot.Families.Sum(static family => family.Games));
    }

    [Fact]
    public async Task MergeRejectsDifferentFormatsAndPreservesFamilies()
    {
        var history = History(
            Standard("DECK_A", MatchResult.Won, Timestamp.AddMinutes(5)),
            Wild("DECK_B", MatchResult.Lost, Timestamp.AddMinutes(10)));
        using var library = new DeckLibrary(CatalogPath());
        await library.InitializeAsync();
        library.ApplyHistory(history);
        var families = library.Snapshot.Families;

        var result = await library.MergeAsync(families[0].Id, families[1].Id);

        Assert.False(result.Success);
        Assert.Contains("нельзя объединить", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, result.Snapshot.Families.Length);
        Assert.All(result.Snapshot.Families, static family => Assert.Single(family.Versions));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string CatalogPath() => Path.Combine(_directory, "decks", "catalog.json");

    private static ProfileHistorySnapshot History(params ProfileEvent[] events) =>
        ProfileHistoryProjection.Create(events);

    private static ProfileEvent Standard(string deckCode, MatchResult result, DateTimeOffset endedAt) =>
        Constructed("standard", deckCode, result, endedAt);

    private static ProfileEvent Wild(string deckCode, MatchResult result, DateTimeOffset endedAt) =>
        Constructed("wild", deckCode, result, endedAt);

    private static ProfileEvent Constructed(
        string format,
        string deckCode,
        MatchResult result,
        DateTimeOffset endedAt) =>
        ProfileEvent.Create(
            ProfileEventType.ConstructedMatch,
            endedAt,
            new ConstructedMatchRecord(
                Guid.CreateVersion7(),
                "ranked",
                format,
                result,
                Certainty.Exact,
                Timestamp,
                endedAt,
                (int)(endedAt - Timestamp).TotalSeconds,
                8,
                "HERO_01",
                "HERO_02",
                new DeckEvidence(deckCode, null, Certainty.Inferred),
                MulliganRecord.Unknown,
                null,
                OpponentDeckEvidence.Unknown,
                null,
                224857,
                2));
}
