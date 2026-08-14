namespace IceCrow.Hearthstone.Protocol.Events;

public sealed record GameCreated(DateTimeOffset Timestamp)
    : GameEvent(Timestamp, BlockId: null);
