namespace IceCrow.ProfileSync.Collection;

/// <summary>
/// Secret-free, card-free diagnostics. Only a short hash prefix and the card
/// count of the last enqueued snapshot are exposed; the card list never is.
/// </summary>
public sealed record CollectionSyncStatus(
    CollectionRefreshTrigger? LastTrigger,
    CollectionSyncOutcome? LastOutcome,
    DateTimeOffset? LastRefreshAt,
    string? LastHashPrefix,
    DateTimeOffset? LastObservedAt,
    int CardCount,
    long RejectedSnapshots,
    long SourceFailures)
{
    public const int HashPrefixLength = 8;

    public static readonly CollectionSyncStatus Initial = new(null, null, null, null, null, 0, 0, 0);

    public static string? PrefixOf(string? contentHash) =>
        contentHash is null ? null : contentHash[..Math.Min(HashPrefixLength, contentHash.Length)];
}
