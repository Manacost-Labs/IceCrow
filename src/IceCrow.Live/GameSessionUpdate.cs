using IceCrow.Hearthstone.Logs;
using IceCrow.Hearthstone.Protocol;
using IceCrow.Tracking;
using IceCrow.Tracking.Constructed;

namespace IceCrow.Live;

/// <summary>
/// Result of routing one raw line through the session coordinator.
/// <see cref="Battlegrounds"/> is null when the line was not routed to the
/// Battlegrounds coordinator; <see cref="CompletedMatch"/> is set exactly once
/// per completed Constructed or Arena game.
/// </summary>
public sealed record GameSessionUpdate(
    RawLogLine RawLine,
    PowerParseResult ParseResult,
    LiveTrackingUpdate? Battlegrounds,
    ConstructedMatchSummary? CompletedMatch,
    GameMetadataState Metadata,
    GameMode ActiveMode,
    bool StateChanged);

/// <summary>Which trackers receive the gameplay lines of the current game.</summary>
public enum GameSessionRoute
{
    Both,
    Battlegrounds,
    Constructed,
    None,
}

public sealed record GameSessionDiagnostics(
    LiveTrackingDiagnostics Battlegrounds,
    long RawLinesReceived,
    long ParsedEvents,
    long Ignored,
    long Unknown,
    long Malformed,
    long BattlegroundsLinesRouted,
    long ConstructedEventsRouted,
    long GamesSeen,
    long CompletedMatches,
    long IgnoredModeGames,
    int ConstructedEntityCount,
    long ConstructedRejectedEntities,
    GameMode ActiveMode,
    GameSessionRoute Route);
