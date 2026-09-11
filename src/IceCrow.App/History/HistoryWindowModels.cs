using System.Globalization;
using IceCrow.App.Runtime;
using IceCrow.ProfileSync;
using IceCrow.ProfileSync.History;
using IceCrow.ProfileSync.Records;

namespace IceCrow.App.History;

internal sealed record MatchHistoryRow(
    HistoryMatch Match,
    string Mode,
    string Outcome,
    string PlayedAt,
    string Summary,
    string PlayerHero,
    string OpponentHero,
    string Deck,
    string Duration,
    string Confidence,
    string SearchText)
{
    public static MatchHistoryRow From(
        HistoryMatch match,
        ActiveDeckSelection? activeDeck = null,
        Func<string, string?>? resolveCardName = null)
    {
        var mode = ModeText(match.Mode);
        var outcome = OutcomeText(match);
        var playerHero = HeroName(match.PlayerHeroCardId, resolveCardName);
        var opponentHero = HeroName(match.OpponentHeroCardId, resolveCardName);
        var deck = DeckName(match, activeDeck);
        return new MatchHistoryRow(
            match,
            mode,
            outcome,
            match.EndedAt.ToLocalTime().ToString("dd.MM.yyyy · HH:mm", CultureInfo.CurrentCulture),
            match.Mode == HistoryGameMode.Battlegrounds
                ? $"{outcome} · ход {match.Turns}"
                : match.Result == MatchResult.Unknown
                    ? $"{deck} · записано ходов: {match.Turns}"
                    : $"{deck} · {match.Turns} ходов",
            playerHero,
            opponentHero,
            deck,
            TimeSpan.FromSeconds(match.DurationSeconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            ConfidenceText(match),
            string.Join(' ', mode, outcome, playerHero, opponentHero, deck));
    }

    public static string ModeText(HistoryGameMode mode) => mode switch
    {
        HistoryGameMode.Standard => "Стандарт",
        HistoryGameMode.Wild => "Вольный",
        HistoryGameMode.Arena => "Арена",
        HistoryGameMode.Battlegrounds => "Поля сражений",
        _ => "Неизвестно",
    };

    private static string OutcomeText(HistoryMatch match)
    {
        if (match.Mode == HistoryGameMode.Battlegrounds)
        {
            return match.Placement is int placement ? $"Место {placement}" : "Итог не найден";
        }

        return match.Result switch
        {
            MatchResult.Won => "Победа",
            MatchResult.Lost => "Поражение",
            MatchResult.Tied => "Ничья",
            _ => "Неполная запись",
        };
    }

    private static string ConfidenceText(HistoryMatch match)
    {
        var certainty = match.Mode == HistoryGameMode.Battlegrounds
            ? match.PlacementConfidence
            : match.ResultConfidence;
        return certainty switch
        {
            Certainty.Exact => "Результат подтверждён журналом игры",
            Certainty.Partial => "Результат записан частично",
            Certainty.Inferred => "Результат восстановлен по косвенным данным",
            _ => "Завершение матча не найдено в журнале игры",
        };
    }

    private static string HeroName(string? cardId, Func<string, string?>? resolveCardName) => cardId switch
    {
        null => "Не определён",
        _ when resolveCardName?.Invoke(cardId) is { Length: > 0 } name => name,
        _ => "Герой определён, название загружается",
    };

    private static string DeckName(HistoryMatch match, ActiveDeckSelection? activeDeck)
    {
        if (match.DeckCode is null && match.DeckHash is null)
        {
            return "Колода не выбрана";
        }

        if (activeDeck is not null &&
            string.Equals(match.DeckCode, activeDeck.Snapshot.DeckCode, StringComparison.Ordinal))
        {
            return activeDeck.Name;
        }

        return "Сохранённая колода";
    }
}

internal sealed record DeckHistoryRow(
    string Mode,
    string Identity,
    string Record,
    string WinRate,
    string LastPlayed,
    string Confidence,
    string? DeckCode)
{
    public static DeckHistoryRow From(
        HistoryDeck deck,
        int index,
        ActiveDeckSelection? activeDeck = null)
    {
        var decided = deck.Wins + deck.Losses + deck.Ties;
        var identity = activeDeck is not null &&
                       string.Equals(deck.DeckCode, activeDeck.Snapshot.DeckCode, StringComparison.Ordinal)
            ? activeDeck.Name
            : $"Колода {index + 1}";
        var unknown = deck.UnknownResults > 0
            ? $" · без итога: {deck.UnknownResults}"
            : string.Empty;
        return new DeckHistoryRow(
        MatchHistoryRow.ModeText(deck.Mode),
        identity,
        $"{deck.Games} матчей · {deck.Wins}–{deck.Losses}{unknown}",
        decided == 0
            ? "Винрейт появится после подтверждённого результата"
            : $"Винрейт {(double)deck.Wins / decided:P1}",
        $"Последняя игра: {deck.LastPlayedAt.ToLocalTime():dd.MM.yyyy HH:mm}",
        deck.Confidence switch
        {
            Certainty.Exact => "Колода подтверждена клиентом",
            Certainty.Inferred => "Выбрана вручную перед матчем",
            _ => "Состав подтверждён частично",
        },
        deck.DeckCode);
    }
}
