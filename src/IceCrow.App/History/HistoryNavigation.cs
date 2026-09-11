using IceCrow.ProfileSync.History;

namespace IceCrow.App.History;

internal enum HistoryPage
{
    Overview,
    Matches,
    Decks,
    Profile,
}

internal sealed record HistoryNavigationRoute(
    HistoryPage Page,
    string Mode,
    string Title,
    string Subtitle);

internal static class HistoryNavigation
{
    public static HistoryNavigationRoute Resolve(string? key) => key switch
    {
        "matches:all" => Matches("all", "Все матчи"),
        "matches:standard" => Matches("standard", "Стандарт"),
        "matches:wild" => Matches("wild", "Вольный режим"),
        "matches:arena" => Matches("arena", "Арена"),
        "matches:battlegrounds" => Matches("battlegrounds", "Поля сражений"),
        "decks" => new(HistoryPage.Decks, "all", "Колоды", "Только подтверждённые составы и результаты"),
        "profile" => new(HistoryPage.Profile, "all", "HearthPulse", "Безопасное подключение профиля через браузер"),
        _ => new(HistoryPage.Overview, "all", "Обзор", "Локальная история работает даже без сервера"),
    };

    public static bool Includes(string requestedMode, HistoryGameMode actualMode) => requestedMode switch
    {
        "standard" => actualMode == HistoryGameMode.Standard,
        "wild" => actualMode == HistoryGameMode.Wild,
        "arena" => actualMode == HistoryGameMode.Arena,
        "battlegrounds" => actualMode == HistoryGameMode.Battlegrounds,
        _ => true,
    };

    private static HistoryNavigationRoute Matches(string mode, string title) =>
        new(HistoryPage.Matches, mode, title, $"История матчей: {title.ToLowerInvariant()}");
}
