using System.Globalization;

namespace IceCrow.Presentation;

/// <summary>
/// Everything the overlay shows about how an opponent's board moved between
/// the two most recent fights. Exists only when the opponent was fought twice.
/// </summary>
public sealed class OpponentChangesViewState : IEquatable<OpponentChangesViewState>
{
    private readonly MinionChangeViewState[] _rows;

    public OpponentChangesViewState(
        int previousTurn,
        int currentTurn,
        bool isMajorChange,
        IEnumerable<MinionChangeViewState> rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(previousTurn);
        ArgumentOutOfRangeException.ThrowIfNegative(currentTurn);
        ArgumentNullException.ThrowIfNull(rows);

        PreviousTurn = previousTurn;
        CurrentTurn = currentTurn;
        IsMajorChange = isMajorChange;
        _rows = rows.ToArray();
        Rows = Array.AsReadOnly(_rows);
    }

    public int PreviousTurn { get; }

    public int CurrentTurn { get; }

    /// <summary>Multiple roster changes or large stat growth since the previous fight.</summary>
    public bool IsMajorChange { get; }

    public IReadOnlyList<MinionChangeViewState> Rows { get; }

    public int ChangeCount => _rows.Length;

    /// <summary>Header line for the detail surface, for example <c>Previous · Turn 7</c>.</summary>
    public string PreviousSeenLine => string.Create(
        CultureInfo.InvariantCulture,
        $"Previous · Turn {PreviousTurn}");

    /// <summary>Detail summary shown when both fights recorded the same board.</summary>
    public string? NoChangeLine => _rows.Length == 0 ? "Board unchanged" : null;

    public bool Equals(OpponentChangesViewState? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
               PreviousTurn == other.PreviousTurn &&
               CurrentTurn == other.CurrentTurn &&
               IsMajorChange == other.IsMajorChange &&
               ViewStateComparison.ListEquals(Rows, other.Rows);
    }

    public override bool Equals(object? obj) => Equals(obj as OpponentChangesViewState);

    public override int GetHashCode() => HashCode.Combine(
        PreviousTurn,
        CurrentTurn,
        IsMajorChange,
        ViewStateComparison.ListHash(Rows));
}
