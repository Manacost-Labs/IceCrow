using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;

namespace IceCrow.Tracking;

/// <summary>
/// Immutable view of the session. <c>LocalBoard</c> is the local warband as
/// it entered its most recent fight; <c>Result</c> exists only once the
/// session has ended and never changes afterwards.
/// </summary>
public sealed record TrackingSnapshot(
    long Revision,
    TrackingSessionState SessionState,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    int EntityCount,
    int TagCount,
    int MaximumTagsOnEntity,
    int TimelineEventCount,
    int MaximumTimelineEventsOnPlayer,
    int OpponentSnapshotCount,
    int MaximumOpponentSnapshotsOnPlayer,
    BattlegroundsState Battlegrounds,
    OpponentMemory OpponentMemory,
    LobbyTimelineSnapshot LobbyTimeline,
    GameMetadataState Metadata,
    BoardSnapshot? LocalBoard,
    BattlegroundsMatchResult? Result);
