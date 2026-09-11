using IceCrow.Hearthstone.ClientState;

namespace IceCrow.App.Runtime;

internal sealed record ActiveDeckSelection(
    string Name,
    string Format,
    SelectedDeckSnapshot Snapshot);

internal sealed record ActiveDeckState(
    ActiveDeckSelection? Selection,
    string Message,
    bool IsError)
{
    public static readonly ActiveDeckState Empty = new(
        null,
        "Выберите колоду перед игрой — IceCrow сохранит её статистику.",
        false);
}

