namespace IceCrow.Hearthstone.Protocol.Events;

public sealed record EntityRevealed(
    DateTimeOffset Timestamp,
    long? BlockId,
    int? EntityId,
    string? EntityName,
    string CardId) : GameEvent(Timestamp, BlockId);
