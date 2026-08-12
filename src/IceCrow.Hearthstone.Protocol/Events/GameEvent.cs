namespace IceCrow.Hearthstone.Protocol.Events;

public abstract record GameEvent(
    DateTimeOffset Timestamp,
    long? BlockId);
