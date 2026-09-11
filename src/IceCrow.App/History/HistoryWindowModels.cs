using System.Globalization;
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
    string Duration,
    string Confidence,
    string SearchText)
{
    public static MatchHistoryRow From(HistoryMatch match)
    {
        var mode = ModeText(match.Mode);
        var outcome = OutcomeText(match);
        var playerHero = match.PlayerHeroCardId ?? "Не определён";
        var opponentHero = match.OpponentHeroCardId ?? "Не определён";
        return new MatchHistoryRow(
            match,
            mode,
            outcome,
            match.EndedAt.ToLocalTime().ToString("dd.MM.yyyy  HH:mm", CultureInfo.CurrentCulture),
            match.Mode == HistoryGameMode.Battlegrounds
                ? $"{outcome} · ход {match.Turns}"
                : $"{outcome} · {match.Turns} ходов",
            playerHero,
            opponentHero,
            TimeSpan.FromSeconds(match.DurationSeconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            ConfidenceText(match),
            string.Join(' ', mode, outcome, playerHero, opponentHero));
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
            return match.Placement is int placement ? $"Место {placement}" : "Место неизвестно";
        }

        return match.Result switch
        {
            MatchResult.Won => "Победа",
            MatchResult.Lost => "Поражение",
            MatchResult.Tied => "Ничья",
            _ => "Результат неизвестен",
        };
    }

    private static string ConfidenceText(HistoryMatch match)
    {
        var certainty = match.Mode == HistoryGameMode.Battlegrounds
            ? match.PlacementConfidence
            : match.ResultConfidence;
        return certainty switch
        {
            Certainty.Exact => "Точно по журналу игры",
            Certainty.Partial => "Частичные данные",
            Certainty.Inferred => "Предположительно",
            _ => "Достоверность неизвестна",
        };
    }
}

internal sealed record DeckHistoryRow(
    string Mode,
    string Identity,
    string Record,
    string LastPlayed,
    string Confidence,
    string? DeckCode)
{
    public static DeckHistoryRow From(HistoryDeck deck) => new(
        MatchHistoryRow.ModeText(deck.Mode),
        deck.DeckHash is { Length: > 8 } hash ? $"Колода {hash[..8]}" : "Известная колода",
        $"{deck.Games} игр · {deck.Wins} побед · {deck.Losses} поражений · {deck.Ties} ничьих",
        $"Последняя игра: {deck.LastPlayedAt.ToLocalTime():dd.MM.yyyy HH:mm}",
        deck.Confidence == Certainty.Exact ? "Точная колода" : "Частичные данные",
        deck.DeckCode);
}
