namespace IceCrow.Hearthstone.ClientState;

public sealed class BattlegroundsClientState : IEquatable<BattlegroundsClientState>
{
    public BattlegroundsClientState(
        ClientBattlegroundsMode mode,
        int? hoveredEntityId = null,
        ClientChoiceState? choice = null)
    {
        if (hoveredEntityId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hoveredEntityId),
                "A hovered Hearthstone entity ID must be positive.");
        }

        Mode = mode;
        HoveredEntityId = hoveredEntityId;
        Choice = choice;
    }

    public ClientBattlegroundsMode Mode { get; }

    public int? HoveredEntityId { get; }

    public ClientChoiceState? Choice { get; }

    public bool Equals(BattlegroundsClientState? other) =>
        other is not null &&
        Mode == other.Mode &&
        HoveredEntityId == other.HoveredEntityId &&
        Equals(Choice, other.Choice);

    public override bool Equals(object? obj) => Equals(obj as BattlegroundsClientState);

    public override int GetHashCode() => HashCode.Combine(Mode, HoveredEntityId, Choice);
}
