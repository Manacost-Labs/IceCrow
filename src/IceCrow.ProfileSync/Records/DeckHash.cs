using System.Security.Cryptography;
using System.Text;

namespace IceCrow.ProfileSync.Records;

/// <summary>
/// Deterministic identity of a card list for <see cref="DeckEvidence.DeckHash"/>:
/// SHA-256 (lowercase hex) over the UTF-8 card ids sorted ordinally, each
/// followed by a newline. Order of observation and duplicates are preserved
/// only through the sorted multiset, so equal decks always hash equally.
/// </summary>
public static class DeckHash
{
    public static string Compute(IEnumerable<string> cardIds)
    {
        ArgumentNullException.ThrowIfNull(cardIds);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var cardId in cardIds.Order(StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(cardId));
            hash.AppendData("\n"u8);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
