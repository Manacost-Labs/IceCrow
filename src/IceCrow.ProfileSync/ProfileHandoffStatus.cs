namespace IceCrow.ProfileSync;

public enum ProfileHandoffPhase
{
    Idle,
    Persisting,

    /// <summary>The outbox refused or failed the last write; the event is held and retried.</summary>
    Retrying,

    /// <summary>The bounded handoff or the outbox is full; new history is refused explicitly.</summary>
    Full,

    /// <summary>Shutdown: accepted events are being written to the outbox before exit.</summary>
    Draining,
    Stopped,
}

/// <summary>
/// Secret-free view of the producer handoff. <c>Accepted</c> counts events
/// the bounded handoff took ownership of; <c>Persisted</c> counts durable
/// commits; the difference is what a crash right now could lose.
/// </summary>
public sealed record ProfileHandoffStatus(
    ProfileHandoffPhase Phase,
    long Accepted,
    long Persisted,
    long Duplicates,
    long RefusedFull,
    long Retries,
    int Pending,
    string? LastFailure)
{
    public static readonly ProfileHandoffStatus Initial = new(ProfileHandoffPhase.Idle, 0, 0, 0, 0, 0, 0, null);
}

public enum ProfileHandoffResult
{
    /// <summary>Owned by the persistence worker until durably committed.</summary>
    Accepted,

    /// <summary>The bounded handoff is full; the caller must surface this.</summary>
    Full,

    /// <summary>The producer side is closed (shutdown in progress).</summary>
    Closed,
}
