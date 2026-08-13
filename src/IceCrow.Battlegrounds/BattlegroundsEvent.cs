using IceCrow.Hearthstone.Entities;

namespace IceCrow.Battlegrounds;

public abstract record BattlegroundsEvent(DateTimeOffset Timestamp);

public sealed record BattlegroundsGameStarted(
    DateTimeOffset Timestamp,
    int? LocalPlayerId = null) : BattlegroundsEvent(Timestamp);

public sealed record BattlegroundsEntityChanged(
    DateTimeOffset Timestamp,
    EntitySnapshot Entity,
    EntityMutation Mutation) : BattlegroundsEvent(Timestamp);

public sealed record BattlegroundsEntityObserved(
    DateTimeOffset Timestamp,
    EntitySnapshot Entity) : BattlegroundsEvent(Timestamp);

public sealed record BattlegroundsGameEnded(
    DateTimeOffset Timestamp) : BattlegroundsEvent(Timestamp);
