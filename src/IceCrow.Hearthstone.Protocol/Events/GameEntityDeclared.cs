namespace IceCrow.Hearthstone.Protocol.Events;

public sealed record GameEntityDeclared(
    DateTimeOffset Timestamp,
    long? BlockId,
    int EntityId) : GameEvent(Timestamp, BlockId);
