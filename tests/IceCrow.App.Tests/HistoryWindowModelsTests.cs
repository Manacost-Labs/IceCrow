using IceCrow.App.History;
using IceCrow.App.Runtime;
using IceCrow.Hearthstone.ClientState;
using IceCrow.ProfileSync;
using IceCrow.ProfileSync.History;
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
