namespace IceCrow.Tracking;

public enum TrackingSafetyLimit
{
    TrackedEntities,
    TagsPerEntity,
    TotalTags,
    LobbyPlayers,
    RetainedText,
}

public sealed class TrackingSafetyLimitExceededException : InvalidOperationException
{
    public TrackingSafetyLimitExceededException(
        TrackingSafetyLimit limit,
        int maximum,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Limit = limit;
        Maximum = maximum;
    }

    public TrackingSafetyLimit Limit { get; }

    public int Maximum { get; }
}
