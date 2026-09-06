namespace IceCrow.ProfileSync.Collection;

public enum CollectionSyncOutcome
{
    /// <summary>The client state source returned nothing (or failed); nothing was written.</summary>
    Unavailable,

    /// <summary>The content hash equals the last enqueued one; the outbox was not touched.</summary>
    Unchanged,

    /// <summary>A new snapshot was queued and nothing older was pending.</summary>
    Enqueued,

    /// <summary>A new snapshot replaced an older pending, unsent snapshot.</summary>
    Replaced,

    /// <summary>
    /// The snapshot is outside the sync contract (for example larger than the
    /// event payload limit) or the outbox refused it; counted, never silent.
    /// </summary>
    Rejected,
}
