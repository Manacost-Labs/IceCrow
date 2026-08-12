namespace IceCrow.Hearthstone.Logs;

public sealed record RawLogLine(
    DateTimeOffset Timestamp,
    string Namespace,
    string Content,
    string OriginalText);
