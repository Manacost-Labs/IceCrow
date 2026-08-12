namespace IceCrow.Hearthstone.Protocol.Events;

public sealed record PlayerEntityDeclared(
    DateTimeOffset Timestamp,
    long? BlockId,
    int EntityId,
    int PlayerId,
    string GameAccountId) : GameEvent(Timestamp, BlockId);
