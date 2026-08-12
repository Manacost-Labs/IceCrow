namespace IceCrow.Hearthstone.Protocol.Events;

public sealed record BlockEnded(
    DateTimeOffset Timestamp,
    PowerBlock Block) : GameEvent(Timestamp, Block.Id);
