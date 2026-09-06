namespace IceCrow.ProfileSync.Arena;

internal enum ArenaDeckChangeKind
{
    Unchanged,

    /// <summary>Exactly one card was added and none removed.</summary>
    SingleGain,

    /// <summary>Anything else: several cards added, cards removed, or both.</summary>
    Other,
}

/// <summary>Multiset comparison of two deck card lists; Arena decks may contain duplicates.</summary>
internal readonly record struct ArenaDeckChange(ArenaDeckChangeKind Kind, string? GainedCardId)
{
    public static ArenaDeckChange Between(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        var expectedGain = current.Count - previous.Count;
        if (expectedGain is < 0 or > 1)
        {
            return new ArenaDeckChange(ArenaDeckChangeKind.Other, null);
        }

        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cardId in previous)
        {
            remaining[cardId] = remaining.GetValueOrDefault(cardId) + 1;
        }

        string? gained = null;
        foreach (var cardId in current)
        {
            if (remaining.TryGetValue(cardId, out var count) && count > 0)
            {
                remaining[cardId] = count - 1;
            }
            else if (gained is null && expectedGain == 1)
            {
                gained = cardId;
            }
            else
            {
                return new ArenaDeckChange(ArenaDeckChangeKind.Other, null);
            }
        }

        return gained is null
            ? new ArenaDeckChange(ArenaDeckChangeKind.Unchanged, null)
            : new ArenaDeckChange(ArenaDeckChangeKind.SingleGain, gained);
    }
}
