using System.Collections.Frozen;

namespace IceCrow.Battlegrounds.Memory;

public sealed class OpponentMemory
{
    private readonly FrozenDictionary<int, OpponentBoardHistory> _histories;

    private OpponentMemory(IReadOnlyDictionary<int, OpponentBoardHistory> histories)
    {
        _histories = histories.ToFrozenDictionary();
    }

    public static OpponentMemory Empty { get; } = new(
        new Dictionary<int, OpponentBoardHistory>());

    public IReadOnlyDictionary<int, OpponentBoardHistory> Histories => _histories;

    public BoardSnapshot? GetLatest(int playerId) =>
        _histories.TryGetValue(playerId, out var history) ? history.Latest : null;

    public OpponentBoardHistory? GetHistory(int playerId) =>
        _histories.GetValueOrDefault(playerId);

    internal OpponentMemory Remember(
        BoardSnapshot snapshot,
        int maximumOpponents,
        int maximumSnapshotsPerOpponent)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOpponents);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSnapshotsPerOpponent);

        if (!_histories.ContainsKey(snapshot.PlayerId) && _histories.Count >= maximumOpponents)
        {
            return this;
        }

        var updated = _histories.ToDictionary();
        updated[snapshot.PlayerId] = updated.TryGetValue(snapshot.PlayerId, out var history)
            ? history.Add(snapshot, maximumSnapshotsPerOpponent)
            : OpponentBoardHistory.Start(snapshot);
        return new OpponentMemory(updated);
    }
}
