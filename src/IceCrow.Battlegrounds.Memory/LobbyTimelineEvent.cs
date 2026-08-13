namespace IceCrow.Battlegrounds.Memory;

public abstract record LobbyTimelineEvent(
    int PlayerId,
    int Turn,
    DateTimeOffset Timestamp);

public sealed record TavernUpgraded(
    int PlayerId,
    int Turn,
    DateTimeOffset Timestamp,
    int TavernTier)
    : LobbyTimelineEvent(PlayerId, Turn, Timestamp);

public sealed record TripleCreated(
    int PlayerId,
    int Turn,
    DateTimeOffset Timestamp,
    int PreviousTotal,
    int Total)
    : LobbyTimelineEvent(PlayerId, Turn, Timestamp)
{
    public int Amount => Total - PreviousTotal;
}

public sealed record DamageTaken(
    int PlayerId,
    int Turn,
    DateTimeOffset Timestamp,
    int PreviousHealth,
    int Health,
    int PreviousArmor,
    int Armor,
    int? ExactDamage)
    : LobbyTimelineEvent(PlayerId, Turn, Timestamp);

public sealed record PlayerDied(
    int PlayerId,
    int Turn,
    DateTimeOffset Timestamp,
    int Health,
    int Armor)
    : LobbyTimelineEvent(PlayerId, Turn, Timestamp);

public sealed record OpponentObserved(
    int PlayerId,
    int Turn,
    DateTimeOffset Timestamp,
    int MinionCount)
    : LobbyTimelineEvent(PlayerId, Turn, Timestamp);

internal sealed class LobbyTimelineEventComparer : IComparer<LobbyTimelineEvent>
{
    public static LobbyTimelineEventComparer Instance { get; } = new();

    public int Compare(LobbyTimelineEvent? left, LobbyTimelineEvent? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var comparison = left.Turn.CompareTo(right.Turn);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Timestamp.CompareTo(right.Timestamp);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.PlayerId.CompareTo(right.PlayerId);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = GetEventOrder(left).CompareTo(GetEventOrder(right));
        return comparison != 0 ? comparison : CompareEventDetails(left, right);
    }

    private static int GetEventOrder(LobbyTimelineEvent timelineEvent) => timelineEvent switch
    {
        TavernUpgraded => 0,
        TripleCreated => 1,
        DamageTaken => 2,
        PlayerDied => 3,
        OpponentObserved => 4,
        _ => int.MaxValue,
    };

    private static int CompareEventDetails(
        LobbyTimelineEvent left,
        LobbyTimelineEvent right) => (left, right) switch
    {
        (TavernUpgraded leftUpgrade, TavernUpgraded rightUpgrade) =>
            leftUpgrade.TavernTier.CompareTo(rightUpgrade.TavernTier),
        (TripleCreated leftTriple, TripleCreated rightTriple) =>
            leftTriple.Total.CompareTo(rightTriple.Total),
        (DamageTaken leftDamage, DamageTaken rightDamage) =>
            CompareDamage(leftDamage, rightDamage),
        (PlayerDied leftDeath, PlayerDied rightDeath) =>
            ComparePair(leftDeath.Health, leftDeath.Armor, rightDeath.Health, rightDeath.Armor),
        (OpponentObserved leftObservation, OpponentObserved rightObservation) =>
            leftObservation.MinionCount.CompareTo(rightObservation.MinionCount),
        _ => 0,
    };

    private static int CompareDamage(DamageTaken left, DamageTaken right)
    {
        var comparison = ComparePair(
            left.PreviousHealth,
            left.Health,
            right.PreviousHealth,
            right.Health);
        return comparison != 0
            ? comparison
            : ComparePair(
                left.PreviousArmor,
                left.Armor,
                right.PreviousArmor,
                right.Armor);
    }

    private static int ComparePair(int leftFirst, int leftSecond, int rightFirst, int rightSecond)
    {
        var comparison = leftFirst.CompareTo(rightFirst);
        return comparison != 0 ? comparison : leftSecond.CompareTo(rightSecond);
    }
}
