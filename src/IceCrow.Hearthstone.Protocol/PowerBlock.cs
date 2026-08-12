namespace IceCrow.Hearthstone.Protocol;

public sealed record PowerBlock(
    long Id,
    long? ParentId,
    int Depth,
    string Type,
    int? EntityId,
    string? EntityName,
    string EffectCardId,
    string Target,
    int? SubOption,
    string? TriggerKeyword);
