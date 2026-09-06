namespace IceCrow.Tracking.Constructed;

public enum ConstructedMatchResult
{
    Unknown,
    Won,
    Lost,
    Tied,
}

/// <summary>
/// One completed ranked Standard/Wild or Arena game as observed from
/// <c>Power.log</c> alone. Every player-relative fact is Unknown (null, empty,
/// or <see cref="EvidenceCertainty.Unknown"/>) when the local player could not
/// be identified; hidden opponent card ids are never recorded, so
/// <see cref="ObservedOpponentCards"/> is evidence, not a deck.
/// </summary>
public sealed record ConstructedMatchSummary(
    GameMode Mode,
    ConstructedFormat Format,
    ConstructedMatchResult Result,
    EvidenceCertainty ResultCertainty,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int Turns,
    int? LocalPlayerId,
    string? PlayerHeroCardId,
    string? OpponentHeroCardId,
    ConstructedMulligan Mulligan,
    int? OpponentMulliganReplacedCount,
    IReadOnlyList<string> ObservedOpponentCards,
    int? HearthstoneBuild,
    int? ScenarioId);
