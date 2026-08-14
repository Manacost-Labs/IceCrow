namespace IceCrow.Tracking;

public sealed record TrackingSessionLimits
{
    public const int DefaultMaximumTrackedEntities = 32_768;
    public const int DefaultTrackedEntityWarningThreshold = 8_192;
    public const int DefaultMaximumTagsPerEntity = 256;
    public const int DefaultTagsPerEntityWarningThreshold = 128;
    public const int DefaultMaximumTotalTags = 1_000_000;
    public const int DefaultTotalTagWarningThreshold = 250_000;
    public const int DefaultMaximumLobbyPlayers = 16;
    public const int DefaultLobbyPlayerWarningThreshold = 12;
    public const int DefaultMaximumTimelineEventsPerPlayer = 512;
    public const int DefaultTimelineEventWarningThreshold = 384;
    public const int DefaultMaximumOpponentSnapshotsPerPlayer = 128;
    public const int DefaultOpponentSnapshotWarningThreshold = 96;

    public TrackingSessionLimits(
        int maximumTrackedEntities = DefaultMaximumTrackedEntities,
        int? trackedEntityWarningThreshold = null,
        int maximumTagsPerEntity = DefaultMaximumTagsPerEntity,
        int? tagsPerEntityWarningThreshold = null,
        int maximumTotalTags = DefaultMaximumTotalTags,
        int? totalTagWarningThreshold = null,
        int maximumLobbyPlayers = DefaultMaximumLobbyPlayers,
        int? lobbyPlayerWarningThreshold = null,
        int maximumTimelineEventsPerPlayer = DefaultMaximumTimelineEventsPerPlayer,
        int? timelineEventWarningThreshold = null,
        int maximumOpponentSnapshotsPerPlayer = DefaultMaximumOpponentSnapshotsPerPlayer,
        int? opponentSnapshotWarningThreshold = null)
    {
        trackedEntityWarningThreshold ??= Math.Min(DefaultTrackedEntityWarningThreshold, maximumTrackedEntities);
        tagsPerEntityWarningThreshold ??= Math.Min(DefaultTagsPerEntityWarningThreshold, maximumTagsPerEntity);
        totalTagWarningThreshold ??= Math.Min(DefaultTotalTagWarningThreshold, maximumTotalTags);
        lobbyPlayerWarningThreshold ??= Math.Min(DefaultLobbyPlayerWarningThreshold, maximumLobbyPlayers);
        timelineEventWarningThreshold ??= Math.Min(DefaultTimelineEventWarningThreshold, maximumTimelineEventsPerPlayer);
        opponentSnapshotWarningThreshold ??= Math.Min(DefaultOpponentSnapshotWarningThreshold, maximumOpponentSnapshotsPerPlayer);

        ValidateLimit(maximumTrackedEntities, trackedEntityWarningThreshold.Value, nameof(maximumTrackedEntities), nameof(trackedEntityWarningThreshold));
        ValidateLimit(maximumTagsPerEntity, tagsPerEntityWarningThreshold.Value, nameof(maximumTagsPerEntity), nameof(tagsPerEntityWarningThreshold));
        ValidateLimit(maximumTotalTags, totalTagWarningThreshold.Value, nameof(maximumTotalTags), nameof(totalTagWarningThreshold));
        ValidateLimit(maximumLobbyPlayers, lobbyPlayerWarningThreshold.Value, nameof(maximumLobbyPlayers), nameof(lobbyPlayerWarningThreshold));
        ValidateLimit(maximumTimelineEventsPerPlayer, timelineEventWarningThreshold.Value, nameof(maximumTimelineEventsPerPlayer), nameof(timelineEventWarningThreshold));
        ValidateLimit(maximumOpponentSnapshotsPerPlayer, opponentSnapshotWarningThreshold.Value, nameof(maximumOpponentSnapshotsPerPlayer), nameof(opponentSnapshotWarningThreshold));

        MaximumTrackedEntities = maximumTrackedEntities;
        TrackedEntityWarningThreshold = trackedEntityWarningThreshold.Value;
        MaximumTagsPerEntity = maximumTagsPerEntity;
        TagsPerEntityWarningThreshold = tagsPerEntityWarningThreshold.Value;
        MaximumTotalTags = maximumTotalTags;
        TotalTagWarningThreshold = totalTagWarningThreshold.Value;
        MaximumLobbyPlayers = maximumLobbyPlayers;
        LobbyPlayerWarningThreshold = lobbyPlayerWarningThreshold.Value;
        MaximumTimelineEventsPerPlayer = maximumTimelineEventsPerPlayer;
        TimelineEventWarningThreshold = timelineEventWarningThreshold.Value;
        MaximumOpponentSnapshotsPerPlayer = maximumOpponentSnapshotsPerPlayer;
        OpponentSnapshotWarningThreshold = opponentSnapshotWarningThreshold.Value;
    }

    public static TrackingSessionLimits Default { get; } = new();

    public int MaximumTrackedEntities { get; }

    public int TrackedEntityWarningThreshold { get; }

    public int MaximumTagsPerEntity { get; }

    public int TagsPerEntityWarningThreshold { get; }

    public int MaximumTotalTags { get; }

    public int TotalTagWarningThreshold { get; }

    public int MaximumLobbyPlayers { get; }

    public int LobbyPlayerWarningThreshold { get; }

    public int MaximumTimelineEventsPerPlayer { get; }

    public int TimelineEventWarningThreshold { get; }

    public int MaximumOpponentSnapshotsPerPlayer { get; }

    public int OpponentSnapshotWarningThreshold { get; }

    private static void ValidateLimit(
        int maximum,
        int warningThreshold,
        string maximumName,
        string warningName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum, maximumName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warningThreshold, warningName);
        if (warningThreshold > maximum)
        {
            throw new ArgumentOutOfRangeException(
                warningName,
                warningThreshold,
                "A diagnostic warning threshold cannot exceed its hard safety limit.");
        }
    }
}
