namespace IceCrow.Presentation;

/// <summary>
/// The complete immutable overlay projection for one tracking snapshot.
/// </summary>
public sealed class BattlegroundsOverlayViewState : IEquatable<BattlegroundsOverlayViewState>
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

    public static BattlegroundsOverlayViewState Empty { get; } = new(showLobby: false, []);

    public bool ShowLobby { get; }

    public IReadOnlyList<OpponentOverlayViewState> Opponents { get; }

    public bool Equals(BattlegroundsOverlayViewState? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
               ShowLobby == other.ShowLobby &&
               ViewStateComparison.ListEquals(Opponents, other.Opponents);
    }

    public override bool Equals(object? obj) => Equals(obj as BattlegroundsOverlayViewState);

    public override int GetHashCode() =>
        HashCode.Combine(ShowLobby, ViewStateComparison.ListHash(Opponents));
}
