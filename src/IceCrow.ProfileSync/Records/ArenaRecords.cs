namespace IceCrow.ProfileSync.Records;

public sealed record ArenaMatchRecord(
    Guid MatchId,
    Guid? RunId,
    int? ScoreBefore,
    int? ScoreAfter,
    Certainty ScoreConfidence,
    MatchResult Result,
    Certainty ResultConfidence,
    string? PlayerHeroCardId,
    string? OpponentHeroCardId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DurationSeconds,
    int Turns,
    MulliganRecord PlayerMulligan,
    int? OpponentMulliganReplacedCount,
    int? HearthstoneBuild);

/// <summary>One draft choice. The client does not always offer exactly three options.</summary>
public sealed record ArenaDraftPickRecord(
    Guid RunId,
    int PickIndex,
    IReadOnlyList<string> OfferedCardIds,
    string ChosenCardId,
    DateTimeOffset ObservedAt,
    Certainty Confidence);

public sealed record ArenaRunRecord(
    Guid RunId,
    string? HeroCardId,
    DeckEvidence FinalDeck,
    IReadOnlyList<string> FinalDeckCardIds,
    int Wins,
    int Losses,
    Certainty ScoreConfidence,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    bool IsComplete,
    int? RatingBefore,
    int? RatingAfter,
    Certainty RatingConfidence);
