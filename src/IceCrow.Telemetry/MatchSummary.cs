namespace IceCrow.Telemetry;

public sealed record TavernProgressionEntry(int TavernTier, int Turn);

public sealed record MatchSummary(
    int TelemetrySchemaVersion,
    Guid MatchId,
    string GameMode,
    string QueueMode,
    string? HearthstonePatch,
    string ClientVersion,
    string? HeroCardId,
    int? Placement,
    string? RatingBucket,
    int Turns,
    IReadOnlyList<TavernProgressionEntry> TavernProgression,
    int Triples,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt);
