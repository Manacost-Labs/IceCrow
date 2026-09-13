using IceCrow.App.History;
using IceCrow.App.Runtime;
using IceCrow.Hearthstone.ClientState;
using IceCrow.ProfileSync;
using IceCrow.ProfileSync.History;
using IceCrow.ProfileSync.History.Decks;
using IceCrow.ProfileSync.Records;

namespace IceCrow.App.Tests;

public sealed class HistoryWindowModelsTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IncompleteMatchUsesHumanStatusAndDoesNotExposeRawCardIds()
    {
        var match = Match(MatchResult.Unknown, Certainty.Unknown, "DECK_CODE");
        var active = new ActiveDeckSelection(
            "Контроль воин",
            "standard",
            new SelectedDeckSnapshot(Timestamp.AddMinutes(-1), "DECK_CODE", null, "standard", []));

        var row = MatchHistoryRow.From(match, active, static _ => null);

        Assert.Equal("Неполная запись", row.Outcome);
        Assert.Equal("Контроль воин", row.Deck);
        Assert.Contains("не найдено", row.Confidence, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Герой определён, название загружается", row.PlayerHero);
        Assert.DoesNotContain("HERO_RAW", row.SearchText, StringComparison.Ordinal);
    }

    [Fact]
    public void DeckRowShowsWinRateWithoutRawHashAndExcludesUnknownResults()
    {
        var deck = new HistoryDeck(
            HistoryGameMode.Standard,
            "DECK_CODE",
            "abcdef0123456789",
            Certainty.Inferred,
            Games: 3,
            Wins: 1,
            Losses: 1,
            Ties: 0,
            UnknownResults: 1,
            Timestamp);
        var active = new ActiveDeckSelection(
            "Контроль воин",
            "standard",
            new SelectedDeckSnapshot(Timestamp.AddMinutes(-1), "DECK_CODE", null, "standard", []));

        var row = DeckHistoryRow.From(deck, 0, active);

        Assert.Equal("Контроль воин", row.Identity);
        Assert.StartsWith("Винрейт 50", row.WinRate, StringComparison.Ordinal);
        Assert.Contains("без итога: 1", row.Record, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdef", row.Identity, StringComparison.Ordinal);
        Assert.Equal("Выбрана вручную перед матчем", row.Confidence);
    }

    [Fact]
    public void DeckFamilyRowSeparatesOverallAndCurrentRevisionStatistics()
    {
        var first = new DeckVersionStatistics(
            DeckRevisionIdentity.FromCode("standard", "DECK_A"),
            4,
            3,
            1,
            0,
            0,
            Timestamp.AddDays(-1),
            Certainty.Inferred,
            "HERO_RAW");
        var current = new DeckVersionStatistics(
            DeckRevisionIdentity.FromCode("standard", "DECK_B"),
            2,
            1,
            1,
            0,
            0,
            Timestamp,
            Certainty.Inferred,
            "HERO_RAW");
        var family = new DeckFamilyStatistics(
            Guid.CreateVersion7(),
            "Контроль воин",
            "standard",
            [first, current],
            current.Identity.Key,
            true,
            6,
            4,
            2,
            0,
            0,
            Timestamp,
            Certainty.Inferred,
            "HERO_RAW");

        var row = DeckFamilyRow.From(family, 0, static _ => "Гаррош");

        Assert.Equal("Контроль воин", row.Name);
        Assert.Equal("6 игр · 4–2", row.Record);
        Assert.Equal("2 игры · 1–1", row.CurrentRecord);
        Assert.Equal("Версий: 2", row.Versions);
        Assert.Equal("АКТИВНА", row.ActiveLabel);
        Assert.DoesNotContain(
            "HERO_RAW",
            string.Join(' ', row.Name, row.Mode, row.Hero, row.Record, row.WinRate, row.Confidence),
            StringComparison.Ordinal);
    }

    private static HistoryMatch Match(MatchResult result, Certainty certainty, string? deckCode) => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        HistoryGameMode.Standard,
        result,
        certainty,
        Timestamp.AddMinutes(-5),
        Timestamp,
        300,
        8,
        "HERO_RAW",
        null,
        null,
        Certainty.Unknown,
        deckCode,
        null,
        deckCode is null ? Certainty.Unknown : Certainty.Inferred);
}
