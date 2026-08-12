namespace IceCrow.Hearthstone.Protocol.Events;

public sealed record BlockStarted(
    DateTimeOffset Timestamp,
    PowerBlock Block) : GameEvent(Timestamp, Block.Id);
