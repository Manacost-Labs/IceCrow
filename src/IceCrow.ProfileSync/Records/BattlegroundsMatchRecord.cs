namespace IceCrow.ProfileSync.Records;

public enum BattlegroundsMode
{
    Unknown,
    Solo,
    Duos,
}

public sealed record FinalBoardMinion(
    int Slot,
    string? CardId,
    int Attack,
    int Health,
    bool? IsGolden);

/// <summary>
/// The local player last authoritative warband. <see cref="Confidence"/> is
/// Exact only when the board was captured in the final combat of the match;
/// an earlier board frozen at completion is Partial.
/// </summary>
public sealed record FinalBoardRecord(
    DateTimeOffset CapturedAt,
    int Turn,
    IReadOnlyList<FinalBoardMinion> Minions,
    Certainty Confidence);

public sealed record BattlegroundsMatchRecord(
    Guid MatchId,
    BattlegroundsMode Mode,
    int? MmrBefore,
    int? MmrAfter,
    Certainty MmrConfidence,
    string? HeroCardId,
    int? Placement,
    Certainty PlacementConfidence,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DurationSeconds,
    int FinalTurn,
    FinalBoardRecord? FinalBoard,
    int? HearthstoneBuild);
