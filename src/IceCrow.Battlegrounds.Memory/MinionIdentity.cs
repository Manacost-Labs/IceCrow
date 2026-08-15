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

    /// <summary>Matched by card id only; the entity was likely recreated between fights.</summary>
    LikelySameCard,
}
