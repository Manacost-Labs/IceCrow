namespace IceCrow.Battlegrounds.Memory;

/// <summary>
/// How confidently two observed minions were matched across board observations.
/// Battlegrounds recreates opponent warband entities for every combat, so an
/// equal entity id is strong evidence but an unequal one proves nothing.
/// </summary>
public enum MinionIdentity
{
    /// <summary>Both observations carry the same entity id and card id.</summary>
    SameEntity,

    /// <summary>
    /// Matched by card id only, and exactly one copy existed on each side, so
    /// the pairing is well-evidenced even though the entity was recreated.
    /// </summary>
    LikelySameCard,

    /// <summary>
    /// Matched by card id inside a group of duplicate copies. The pairing is
    /// deterministic but arbitrary: per-minion deltas must not be presented as
    /// facts, only the roster arithmetic is certain.
    /// </summary>
    AmbiguousCardCopy,
}
