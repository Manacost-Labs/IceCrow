namespace IceCrow.Hearthstone.Protocol.Events;

public sealed record RawTagChanged(
    DateTimeOffset Timestamp,
    long? BlockId,
    int? EntityId,
    string? EntityName,
    string Tag,
    string Value,
    bool IsCreationTag) : GameEvent(Timestamp, BlockId);
