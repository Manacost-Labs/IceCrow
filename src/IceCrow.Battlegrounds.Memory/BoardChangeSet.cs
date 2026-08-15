using System.Collections.ObjectModel;

namespace IceCrow.Battlegrounds.Memory;

/// <summary>
/// Immutable comparison of two observed boards of the same opponent:
/// what appeared, what disappeared, and what visibly changed in between.
/// </summary>
public sealed class BoardChangeSet
{
    /// <summary>Added or removed minions that mark the change as major.</summary>
    public const int MajorRosterChangeCount = 2;

    /// <summary>Total positive attack+health growth that marks the change as major.</summary>
    public const int MajorStatGrowthThreshold = 20;

    private readonly ReadOnlyCollection<MinionSnapshot> _added;
    private readonly ReadOnlyCollection<MinionSnapshot> _removed;
    private readonly ReadOnlyCollection<MinionChange> _changed;

    internal BoardChangeSet(
        int previousTurn,
        int currentTurn,
        MinionSnapshot[] added,
        MinionSnapshot[] removed,
        MinionChange[] changed)
    {
        PreviousTurn = previousTurn;
        CurrentTurn = currentTurn;
        _added = Array.AsReadOnly(added);
        _removed = Array.AsReadOnly(removed);
        _changed = Array.AsReadOnly(changed);
        StatGrowth = SumPositiveDeltas(changed);
    }

    public int PreviousTurn { get; }

    public int CurrentTurn { get; }

    /// <summary>Minions on the latest board with no counterpart on the previous board.</summary>
    public IReadOnlyList<MinionSnapshot> AddedMinions => _added;

    /// <summary>Minions on the previous board with no counterpart on the latest board.</summary>
    public IReadOnlyList<MinionSnapshot> RemovedMinions => _removed;

    /// <summary>Matched minions whose stats or position changed.</summary>
    public IReadOnlyList<MinionChange> ChangedMinions => _changed;

    /// <summary>Sum of the positive attack and health deltas across matched minions.</summary>
    public int StatGrowth { get; }

    public bool HasChanges =>
        _added.Count > 0 || _removed.Count > 0 || _changed.Count > 0;

    public BoardChangeSignificance Significance => !HasChanges
        ? BoardChangeSignificance.NoChange
        : _added.Count >= MajorRosterChangeCount ||
          _removed.Count >= MajorRosterChangeCount ||
          StatGrowth >= MajorStatGrowthThreshold
            ? BoardChangeSignificance.Major
            : BoardChangeSignificance.Minor;

    private static int SumPositiveDeltas(MinionChange[] changed)
    {
        var growth = 0;
        foreach (var change in changed)
        {
            growth += Math.Max(0, change.AttackDelta) + Math.Max(0, change.HealthDelta);
        }

        return growth;
    }
}
