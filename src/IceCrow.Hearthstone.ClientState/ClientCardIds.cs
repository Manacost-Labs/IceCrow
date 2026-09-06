using System.Collections.ObjectModel;

namespace IceCrow.Hearthstone.ClientState;

/// <summary>
/// Shared bounds for card identifiers and opaque client tokens carried by the
/// current-client snapshots. Every snapshot copies its input so that a mutable
/// source collection can never change an immutable observation afterwards.
/// </summary>
internal static class ClientCardIds
{
    public const int MaximumCardIdLength = 64;

    public static string[] CopyBounded(IEnumerable<string> cardIds, int maximumCount, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(cardIds, parameterName);
        var copy = cardIds.Take(maximumCount + 1).ToArray();
        if (copy.Length > maximumCount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The list cannot contain more than {maximumCount} card IDs.");
        }

        foreach (var cardId in copy)
        {
            ValidateCardId(cardId, parameterName);
        }

        return copy;
    }

    public static ReadOnlyCollection<string> AsReadOnly(string[] cardIds) => Array.AsReadOnly(cardIds);

    public static void ValidateCardId(string? cardId, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(cardId) || cardId.Length > MaximumCardIdLength)
        {
            throw new ArgumentException(
                $"Card IDs must contain 1 to {MaximumCardIdLength} non-whitespace characters.",
                parameterName);
        }
    }

    public static string? ValidateOptionalToken(string? value, int maximumLength, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"When present the value must contain 1 to {maximumLength} non-whitespace characters.",
                parameterName);
        }

        return value;
    }

    public static int? ValidateOptionalRange(int? value, int minimum, int maximum, string parameterName)
    {
        if (value is { } present && (present < minimum || present > maximum))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The value must be between {minimum} and {maximum} when present.");
        }

        return value;
    }

    public static bool SequenceEquals(string[] left, string[] right) =>
        left.AsSpan().SequenceEqual(right, StringComparer.Ordinal);

    public static void AddToHash(ref HashCode hash, string[] cardIds)
    {
        hash.Add(cardIds.Length);
        foreach (var cardId in cardIds)
        {
            hash.Add(cardId, StringComparer.Ordinal);
        }
    }
}
