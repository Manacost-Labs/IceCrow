using IceCrow.Battlegrounds;
using IceCrow.Tracking;

namespace IceCrow.Live;

public sealed record LiveTrackingDiagnostics(
    long RawLinesReceived,
    long ParsedEvents,
    long Ignored,
    long Unknown,
    long Malformed,
    long TrackingEventsApplied,
    long BufferedEventsDropped,
    long SafetyLimitRejections,
    long PlayerIdentityEvictions,
    TrackingSessionState TrackingState,
    bool IsBattlegroundsActive,
    int Turn,
    BattlegroundsPhase Phase,
    int LobbyCount,
    int EntityCount,
    int TagCount,
    int MaximumTagsOnEntity,
    int TimelineEventCount,
    int MaximumTimelineEventsOnPlayer,
    int OpponentSnapshotCount,
    int MaximumOpponentSnapshotsOnPlayer,
    LiveTrackingWarnings Warnings,
    int? CurrentOpponentPlayerId,
    DateTimeOffset? LastStateUpdateTimestamp);

[Flags]
public enum LiveTrackingWarnings
{
    None = 0,
    TrackedEntities = 1 << 0,
    TagsPerEntity = 1 << 1,
    TotalTags = 1 << 2,
    LobbyPlayers = 1 << 3,
    TimelineEvents = 1 << 4,
    OpponentSnapshots = 1 << 5,
    LifecyclePlayerIdentities = 1 << 6,
}
