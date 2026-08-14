namespace IceCrow.Presentation;

public sealed class BattlegroundsOverlayViewState
{
    private readonly OpponentOverlayViewState[] _opponents;

    public BattlegroundsOverlayViewState(
        bool showLobby,
        IEnumerable<OpponentOverlayViewState> opponents)
    {
        ArgumentNullException.ThrowIfNull(opponents);
        _opponents = opponents.ToArray();
        ShowLobby = showLobby;
        Opponents = Array.AsReadOnly(_opponents);
    }

    public bool ShowLobby { get; }

    public IReadOnlyList<OpponentOverlayViewState> Opponents { get; }
}
