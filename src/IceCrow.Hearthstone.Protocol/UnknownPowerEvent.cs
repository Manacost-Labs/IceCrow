using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Hearthstone.Protocol;

public sealed record UnknownPowerEvent(
    DateTimeOffset Timestamp,
    long? BlockId,
    string Content) : GameEvent(Timestamp, BlockId);
