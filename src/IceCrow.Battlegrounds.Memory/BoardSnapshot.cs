using System.Collections.ObjectModel;
using IceCrow.Hearthstone.Entities;

namespace IceCrow.Battlegrounds.Memory;

public sealed class BoardSnapshot
{
    private readonly MinionSnapshot[] _minions;
    private readonly ReadOnlyCollection<MinionSnapshot> _readOnlyMinions;

    private BoardSnapshot(
        int playerId,
        int turn,
        DateTimeOffset timestamp,
        MinionSnapshot[] minions)
    {
        PlayerId = playerId;
        Turn = turn;
        Timestamp = timestamp;
        _minions = minions;
        _readOnlyMinions = Array.AsReadOnly(_minions);
    }

    public int PlayerId { get; }

    public int Turn { get; }

    public DateTimeOffset Timestamp { get; }

    public IReadOnlyList<MinionSnapshot> Minions => _readOnlyMinions;

    public static BoardSnapshot Capture(
        int playerId,
        int turn,
        DateTimeOffset timestamp,
        IEnumerable<EntitySnapshot> entities)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        ArgumentOutOfRangeException.ThrowIfNegative(turn);
        ArgumentNullException.ThrowIfNull(entities);

        var minions = entities
            .Where(entity =>
                entity.IsMinion &&
                entity.IsInPlay &&
                entity.Controller == playerId)
            .Select(MinionSnapshot.FromEntity)
            .OrderBy(static minion => minion.ZonePosition)
            .ThenBy(static minion => minion.EntityId)
            .ToArray();

        return new BoardSnapshot(playerId, turn, timestamp, minions);
    }

    public int GetAge(int currentTurn) => Math.Max(0, currentTurn - Turn);
}
