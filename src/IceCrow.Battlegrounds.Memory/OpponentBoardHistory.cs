using System.Collections.ObjectModel;

namespace IceCrow.Battlegrounds.Memory;

public sealed class OpponentBoardHistory
{
    private readonly BoardSnapshot[] _snapshots;
    private readonly ReadOnlyCollection<BoardSnapshot> _readOnlySnapshots;

    private OpponentBoardHistory(int playerId, BoardSnapshot[] snapshots)
    {
        PlayerId = playerId;
        _snapshots = snapshots;
        _readOnlySnapshots = Array.AsReadOnly(_snapshots);
    }

    public int PlayerId { get; }

    public IReadOnlyList<BoardSnapshot> Snapshots => _readOnlySnapshots;

    public BoardSnapshot Latest => _snapshots[^1];

    /// <summary>The fight before <see cref="Latest"/>, if the opponent was fought twice.</summary>
    public BoardSnapshot? Previous => _snapshots.Length > 1 ? _snapshots[^2] : null;

    public static OpponentBoardHistory Start(BoardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new OpponentBoardHistory(snapshot.PlayerId, [snapshot]);
    }

    public OpponentBoardHistory Add(BoardSnapshot snapshot, int maximumSnapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSnapshots);
        if (snapshot.PlayerId != PlayerId)
        {
            throw new ArgumentException(
                "A board snapshot cannot be added to another opponent's history.",
                nameof(snapshot));
        }

        var updatedLength = Math.Min(_snapshots.Length + 1, maximumSnapshots);
        var updated = new BoardSnapshot[updatedLength];
        var retainedCount = updatedLength - 1;
        var sourceIndex = Math.Max(0, _snapshots.Length - retainedCount);
        Array.Copy(_snapshots, sourceIndex, updated, 0, retainedCount);
        updated[^1] = snapshot;
        return new OpponentBoardHistory(PlayerId, updated);
    }
}
