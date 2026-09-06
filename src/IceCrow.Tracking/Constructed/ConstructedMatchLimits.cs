namespace IceCrow.Tracking.Constructed;

/// <summary>
/// Bounds for the lightweight Constructed entity table and for the lists a
/// summary may carry. Exceeding the entity bound is deterministic: entities
/// beyond it are counted and ignored, never partially tracked.
/// </summary>
public sealed record ConstructedMatchLimits
{
    public const int DefaultMaximumTrackedEntities = 4096;
    public const int DefaultMaximumObservedOpponentCards = 64;
    public const int DefaultMaximumMulliganCards = 10;
    public const int MaximumTrackedEntityNames = 512;
    public const int MaximumEntityNameLength = 128;
    public const int MaximumCardIdLength = 64;
    public const int MaximumPlayers = 16;

    public ConstructedMatchLimits(
        int maximumTrackedEntities = DefaultMaximumTrackedEntities,
        int maximumObservedOpponentCards = DefaultMaximumObservedOpponentCards,
        int maximumMulliganCards = DefaultMaximumMulliganCards)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTrackedEntities);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumObservedOpponentCards);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMulliganCards);
        MaximumTrackedEntities = maximumTrackedEntities;
        MaximumObservedOpponentCards = maximumObservedOpponentCards;
        MaximumMulliganCards = maximumMulliganCards;
    }

    public static ConstructedMatchLimits Default { get; } = new();

    public int MaximumTrackedEntities { get; }

    public int MaximumObservedOpponentCards { get; }

    public int MaximumMulliganCards { get; }
}
