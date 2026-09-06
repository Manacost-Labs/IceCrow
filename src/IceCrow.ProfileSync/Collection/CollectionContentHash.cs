using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IceCrow.Hearthstone.ClientState;
using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Collection;

/// <summary>
/// Deterministic normalization and hashing of a collection snapshot. The hash
/// is SHA-256 (lowercase hex) over UTF-8 canonical lines of the form
/// <c>cardId|normal|golden|signature|diamond\n</c> in ordinal card-id order,
/// where an absent optional count is written as <c>-</c>. Two snapshots with
/// the same owned cards therefore hash identically regardless of the order or
/// duplication the client reported them in.
/// </summary>
public static class CollectionContentHash
{
    private const char AbsentCount = '-';

    public static IReadOnlyList<CollectionCardRecord> Normalize(CollectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var merged = new Dictionary<string, CollectionCardRecord>(StringComparer.Ordinal);
        foreach (var card in snapshot.Cards)
        {
            merged[card.CardId] = merged.TryGetValue(card.CardId, out var existing)
                ? new CollectionCardRecord(
                    card.CardId,
                    existing.NormalCount + card.NormalCount,
                    existing.GoldenCount + card.GoldenCount,
                    SumOptional(existing.SignatureCount, card.SignatureCount),
                    SumOptional(existing.DiamondCount, card.DiamondCount))
                : new CollectionCardRecord(card.CardId, card.NormalCount, card.GoldenCount, card.SignatureCount, card.DiamondCount);
        }

        return merged.Values
            .Where(static card => card.NormalCount + card.GoldenCount + (card.SignatureCount ?? 0) + (card.DiamondCount ?? 0) > 0)
            .OrderBy(static card => card.CardId, StringComparer.Ordinal)
            .Take(ProfileRecordLimits.MaximumCollectionCards)
            .ToArray();
    }

    public static string Compute(IReadOnlyList<CollectionCardRecord> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var line = new StringBuilder(128);
        foreach (var card in cards)
        {
            line.Clear();
            line.Append(card.CardId).Append('|');
            line.Append(card.NormalCount.ToString(CultureInfo.InvariantCulture)).Append('|');
            line.Append(card.GoldenCount.ToString(CultureInfo.InvariantCulture)).Append('|');
            AppendOptional(line, card.SignatureCount).Append('|');
            AppendOptional(line, card.DiamondCount).Append('\n');
            hash.AppendData(Encoding.UTF8.GetBytes(line.ToString()));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static StringBuilder AppendOptional(StringBuilder line, int? count) =>
        count is { } present ? line.Append(present.ToString(CultureInfo.InvariantCulture)) : line.Append(AbsentCount);

    private static int? SumOptional(int? left, int? right) =>
        left is null ? right : right is null ? left : left + right;
}
