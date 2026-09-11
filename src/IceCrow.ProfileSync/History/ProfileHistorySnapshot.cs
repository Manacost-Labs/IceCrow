using System.Collections.Immutable;
using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.History;

public enum HistoryGameMode
{
    Standard,
    Wild,
    Arena,
    Battlegrounds,
}

/// <summary>Immutable user-facing evidence for one retained game record.</summary>
public sealed record HistoryMatch(
    Guid EventId,
    Guid MatchId,
    HistoryGameMode Mode,
    MatchResult Result,
    Certainty ResultConfidence,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DurationSeconds,
    int Turns,
    string? PlayerHeroCardId,
    string? OpponentHeroCardId,
    int? Placement,
    Certainty PlacementConfidence,
    string? DeckCode,
    string? DeckHash,
    Certainty DeckConfidence);

/// <summary>Aggregate of games that carried the same observed own-deck identity.</summary>
public sealed record HistoryDeck(
    HistoryGameMode Mode,
    string? DeckCode,
    string? DeckHash,
    Certainty Confidence,
    int Games,
    int Wins,
    int Losses,
    int Ties,
    int UnknownResults,
    DateTimeOffset LastPlayedAt);

/// <summary>Bounded immutable read model. No WPF or mutable entity enters history.</summary>
public sealed record ProfileHistorySnapshot(
    ImmutableArray<HistoryMatch> Matches,
    ImmutableArray<HistoryDeck> Decks,
    int SourceEvents,
    int SkippedEvents,
    bool RecoveredTruncatedTail)
{
    public static readonly ProfileHistorySnapshot Empty = new([], [], 0, 0, false);

    public int Wins => Matches.Count(static match => match.Result == MatchResult.Won);

    public int Losses => Matches.Count(static match => match.Result == MatchResult.Lost);

    public int BattlegroundsGames => Matches.Count(static match => match.Mode == HistoryGameMode.Battlegrounds);

    public int MatchesWithResult => Matches.Count(static match =>
        match.Mode == HistoryGameMode.Battlegrounds
            ? match.Placement is not null
            : match.Result != MatchResult.Unknown);
}
