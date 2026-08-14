namespace IceCrow.Hearthstone.Data;

public sealed record CardQuery(
    int? TavernTier = null,
    string? CreatureType = null,
    bool? IsInPool = null,
    bool IncludeDuosOnly = true,
    string? SearchText = null);
