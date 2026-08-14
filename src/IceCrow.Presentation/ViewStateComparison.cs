namespace IceCrow.Presentation;

/// <summary>
/// Structural comparison helpers for immutable overlay view states. Value
/// equality is what lets the overlay skip WPF work when a tracking snapshot
/// changed but nothing visible did.
/// </summary>
internal static class ViewStateComparison
{
    private const int HashSampleLimit = 8;

    public static bool ListEquals<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; index < left.Count; index++)
        {
            if (!comparer.Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    public static int ListHash<T>(IReadOnlyList<T> values)
    {
        var hash = default(HashCode);
        hash.Add(values.Count);
        var sampled = Math.Min(values.Count, HashSampleLimit);
        for (var index = 0; index < sampled; index++)
        {
            hash.Add(values[index]);
        }

        return hash.ToHashCode();
    }
}
