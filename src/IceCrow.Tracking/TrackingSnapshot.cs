using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;

namespace IceCrow.Tracking;

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
    LobbyTimelineSnapshot LobbyTimeline);
