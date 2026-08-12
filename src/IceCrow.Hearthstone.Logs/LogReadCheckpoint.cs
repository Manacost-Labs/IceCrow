namespace IceCrow.Hearthstone.Logs;

public sealed record LogReadCheckpoint(
    string? FilePath,
    long ByteOffset,
    DateTimeOffset FileCreatedAt,
    long ObservedLength,
    DateTimeOffset LastWriteAt)
{
    public static LogReadCheckpoint Empty { get; } = new(
        null,
        0,
        DateTimeOffset.MinValue,
        0,
        DateTimeOffset.MinValue);
}
