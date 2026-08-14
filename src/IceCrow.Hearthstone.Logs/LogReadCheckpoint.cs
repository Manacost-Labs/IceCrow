namespace IceCrow.Hearthstone.Logs;

public sealed record LogReadCheckpoint(
    string? FilePath,
    long ByteOffset,
    DateTimeOffset FileCreatedAt,
    long ObservedLength,
    DateTimeOffset LastWriteAt,
    ulong PrefixFingerprint,
    int PrefixFingerprintLength)
{
    public static LogReadCheckpoint Empty { get; } = new(
        null,
        0,
        DateTimeOffset.MinValue,
        0,
        DateTimeOffset.MinValue,
        0,
        0);
}
