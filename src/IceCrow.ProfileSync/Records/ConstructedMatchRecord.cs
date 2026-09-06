namespace IceCrow.ProfileSync.Records;

public enum MatchResult
{
    Unknown,
    Won,
    Lost,
    Tied,
}

/// <summary>
/// One completed ranked Standard/Wild game. <see cref="GameJoinEvidence"/> is
/// an authoritative server game handle when a source exposes one; it is null
/// (never approximated from timestamps) when no such source exists.
/// </summary>
public sealed record ConstructedMatchRecord(
    Guid MatchId,
    string GameType,
    string Format,
    MatchResult Result,
    Certainty ResultConfidence,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DurationSeconds,
    int Turns,
    string? PlayerHeroCardId,
    string? OpponentHeroCardId,
    DeckEvidence PlayerDeck,
    MulliganRecord PlayerMulligan,
    int? OpponentMulliganReplacedCount,
    OpponentDeckEvidence OpponentDeck,
    string? GameJoinEvidence,
    int? HearthstoneBuild,
    int? ScenarioId);
