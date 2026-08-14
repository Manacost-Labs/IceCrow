namespace IceCrow.Hearthstone.Data;

public sealed record BattlegroundsHeroDefinition(
    int DbfId,
    string CardId,
    string Name,
    string? EnglishName,
    int? Health,
    int? Armor,
    int? DuosArmor,
    HeroPowerDefinition? HeroPower,
    int? BuddyDbfId,
    CardImageInfo Images);
