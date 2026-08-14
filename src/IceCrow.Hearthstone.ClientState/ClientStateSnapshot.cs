namespace IceCrow.Hearthstone.ClientState;

public sealed class ClientStateSnapshot : IEquatable<ClientStateSnapshot>
{
    public ClientStateSnapshot(
        DateTimeOffset observedAt,
        ClientStateProviderStatus status,
        ClientStateCapabilities availableCapabilities,
        BattlegroundsClientState? battlegrounds = null)
    {
        if (status is not (ClientStateProviderStatus.Connected or ClientStateProviderStatus.Partial))
        {
            if (availableCapabilities != ClientStateCapabilities.None || battlegrounds is not null)
            {
                throw new ArgumentException(
                    "A non-connected snapshot cannot retain capabilities or client state.",
                    nameof(availableCapabilities));
            }
        }
        else
        {
            if (availableCapabilities == ClientStateCapabilities.None)
            {
                throw new ArgumentException(
                    "A connected snapshot must expose at least one available capability.",
                    nameof(availableCapabilities));
            }

            if (battlegrounds is null)
            {
                throw new ArgumentNullException(
                    nameof(battlegrounds),
                    "The current v1 capabilities all belong to Battlegrounds client state.");
            }
        }

        ObservedAt = observedAt;
        Status = status;
        AvailableCapabilities = availableCapabilities;
        Battlegrounds = battlegrounds;
    }

    public DateTimeOffset ObservedAt { get; }

    public ClientStateProviderStatus Status { get; }

    public ClientStateCapabilities AvailableCapabilities { get; }

    public BattlegroundsClientState? Battlegrounds { get; }

    public static ClientStateSnapshot WithoutClientState(
        DateTimeOffset observedAt,
        ClientStateProviderStatus status)
    {
        if (status is ClientStateProviderStatus.Connected or ClientStateProviderStatus.Partial)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                "Connected states require capability data.");
        }

        return new ClientStateSnapshot(
            observedAt,
            status,
            ClientStateCapabilities.None);
    }

    public bool SemanticallyEquals(ClientStateSnapshot? other) =>
        other is not null &&
        Status == other.Status &&
        AvailableCapabilities == other.AvailableCapabilities &&
        Equals(Battlegrounds, other.Battlegrounds);

    public bool Equals(ClientStateSnapshot? other) =>
        other is not null &&
        ObservedAt == other.ObservedAt &&
        SemanticallyEquals(other);

    public override bool Equals(object? obj) => Equals(obj as ClientStateSnapshot);

    public override int GetHashCode() =>
        HashCode.Combine(ObservedAt, Status, AvailableCapabilities, Battlegrounds);
}
