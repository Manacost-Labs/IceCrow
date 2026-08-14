namespace IceCrow.Hearthstone.ClientState;

public sealed record ClientStateChange(
    ClientStateSnapshot? Previous,
    ClientStateSnapshot Current,
    ClientStateCapabilities ChangedCapabilities,
    bool ProviderStatusChanged)
{
    internal static ClientStateChange Between(
        ClientStateSnapshot? previous,
        ClientStateSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var changed = previous is null
            ? current.AvailableCapabilities
            : previous.AvailableCapabilities ^ current.AvailableCapabilities;

        if (previous?.Battlegrounds?.Mode != current.Battlegrounds?.Mode)
        {
            changed |= ClientStateCapabilities.BattlegroundsMode;
        }

        if (previous?.Battlegrounds?.HoveredEntityId != current.Battlegrounds?.HoveredEntityId)
        {
            changed |= ClientStateCapabilities.HoveredOpponent;
        }

        if (!Equals(previous?.Battlegrounds?.Choice, current.Battlegrounds?.Choice))
        {
            changed |= ClientStateCapabilities.Choices;
        }

        return new ClientStateChange(
            previous,
            current,
            changed,
            previous?.Status != current.Status);
    }
}
