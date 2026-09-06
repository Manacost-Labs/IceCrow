namespace IceCrow.Tracking.Constructed;

/// <summary>
/// Result of applying one normalized event to the Constructed tracker.
/// <see cref="CompletedMatch"/> is set exactly once per completed supported
/// game, on the event that completed it.
/// </summary>
public sealed record ConstructedTrackerUpdate(
    ConstructedMatchSummary? CompletedMatch,
    GameMetadataState Metadata)
{
    public bool MatchCompleted => CompletedMatch is not null;
}
