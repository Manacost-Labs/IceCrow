namespace IceCrow.Hearthstone.Data;

public sealed record HeroPowerDefinition(
    int? DbfId,
    string? Name,
    string? Text,
    CardImageInfo Images);
