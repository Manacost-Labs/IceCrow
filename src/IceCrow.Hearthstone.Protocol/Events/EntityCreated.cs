namespace IceCrow.Hearthstone.Protocol.Events;

public sealed record EntityCreated(
    DateTimeOffset Timestamp,
    long? BlockId,
    int EntityId,
    string CardId) : GameEvent(Timestamp, BlockId);
