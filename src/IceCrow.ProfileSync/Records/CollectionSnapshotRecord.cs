namespace IceCrow.ProfileSync.Records;

public sealed record CollectionCardRecord(
    string CardId,
    int NormalCount,
    int GoldenCount,
    int? SignatureCount,
    int? DiamondCount);

/// <summary>
/// Latest-state collection snapshot. <see cref="ContentHash"/> is the
/// deterministic hash of the normalized card list so unchanged collections
/// are never re-sent.
/// </summary>
public sealed record CollectionSnapshotRecord(
    DateTimeOffset ObservedAt,
    string ContentHash,
    IReadOnlyList<CollectionCardRecord> Cards);
