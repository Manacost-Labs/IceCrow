namespace IceCrow.Battlegrounds.Memory;

/// <summary>
/// Deterministic comparison of two board observations of the same opponent.
/// Matching is explicit two-pass work over at most a handful of minions:
/// exact entity matches first, then card-id matches in board order.
/// </summary>
public static class OpponentBoardDiffCalculator
{
    public static BoardChangeSet Compare(BoardSnapshot previous, BoardSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (previous.PlayerId != current.PlayerId)
        {
            throw new ArgumentException(
                "Board snapshots of different opponents cannot be compared.",
                nameof(current));
        }

        var previousMinions = previous.Minions;
        var currentMinions = current.Minions;
        var previousMatch = new int[previousMinions.Count];
        var currentIdentity = new MinionIdentity?[currentMinions.Count];
        Array.Fill(previousMatch, -1);

        MatchByEntity(previousMinions, currentMinions, previousMatch, currentIdentity);
        MatchByCard(previousMinions, currentMinions, previousMatch, currentIdentity);

        return Collect(previous, current, previousMatch, currentIdentity);
    }

    private static void MatchByEntity(
        IReadOnlyList<MinionSnapshot> previousMinions,
        IReadOnlyList<MinionSnapshot> currentMinions,
        int[] previousMatch,
        MinionIdentity?[] currentIdentity)
    {
        for (var currentIndex = 0; currentIndex < currentMinions.Count; currentIndex++)
        {
            var minion = currentMinions[currentIndex];
            for (var previousIndex = 0; previousIndex < previousMinions.Count; previousIndex++)
            {
                var candidate = previousMinions[previousIndex];
                if (previousMatch[previousIndex] < 0 &&
                    candidate.EntityId == minion.EntityId &&
                    string.Equals(candidate.CardId, minion.CardId, StringComparison.Ordinal))
                {
                    previousMatch[previousIndex] = currentIndex;
                    currentIdentity[currentIndex] = MinionIdentity.SameEntity;
                    break;
                }
            }
        }
    }

    private static void MatchByCard(
        IReadOnlyList<MinionSnapshot> previousMinions,
        IReadOnlyList<MinionSnapshot> currentMinions,
        int[] previousMatch,
        MinionIdentity?[] currentIdentity)
    {
        for (var currentIndex = 0; currentIndex < currentMinions.Count; currentIndex++)
        {
            var minion = currentMinions[currentIndex];
            if (currentIdentity[currentIndex] is not null ||
                string.IsNullOrEmpty(minion.CardId))
            {
                continue;
            }

            for (var previousIndex = 0; previousIndex < previousMinions.Count; previousIndex++)
            {
                var candidate = previousMinions[previousIndex];
                if (previousMatch[previousIndex] < 0 &&
                    string.Equals(candidate.CardId, minion.CardId, StringComparison.Ordinal))
                {
                    previousMatch[previousIndex] = currentIndex;
                    currentIdentity[currentIndex] = MinionIdentity.LikelySameCard;
                    break;
                }
            }
        }
    }

    private static BoardChangeSet Collect(
        BoardSnapshot previous,
        BoardSnapshot current,
        int[] previousMatch,
        MinionIdentity?[] currentIdentity)
    {
        var added = new List<MinionSnapshot>();
        var changed = new List<MinionChange>();
        for (var currentIndex = 0; currentIndex < currentIdentity.Length; currentIndex++)
        {
            if (currentIdentity[currentIndex] is null)
            {
                added.Add(current.Minions[currentIndex]);
            }
        }

        var removed = new List<MinionSnapshot>();
        for (var previousIndex = 0; previousIndex < previousMatch.Length; previousIndex++)
        {
            var currentIndex = previousMatch[previousIndex];
            if (currentIndex < 0)
            {
                removed.Add(previous.Minions[previousIndex]);
                continue;
            }

            var change = new MinionChange(
                previous.Minions[previousIndex],
                current.Minions[currentIndex],
                currentIdentity[currentIndex]!.Value);
            if (change.HasStatChange || change.HasPositionChange)
            {
                changed.Add(change);
            }
        }

        return new BoardChangeSet(
            previous.Turn,
            current.Turn,
            [.. added],
            [.. removed],
            [.. changed]);
    }
}
