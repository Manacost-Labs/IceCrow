namespace IceCrow.Hearthstone.Data;

public sealed record CardDefinition(
    int DbfId,
    string CardId,
    string Name,
    string? EnglishName,
    string? Text,
    string CardType,
    int? Cost,
    int? TavernTier,
    IReadOnlyList<string> CreatureTypes,
    bool IsInPool,
    bool IsDuosOnly,
    string? GoldenCardId,
    CardImageInfo Images);
