namespace IceCrow.ProfileSync;

public enum ProfileSyncPhase
{
    /// <summary>No device credential; nothing is uploaded.</summary>
    NotLinked,
    Idle,
    Uploading,
    BackingOff,

    /// <summary>The credential was rejected; the user must link the device again.</summary>
    AuthorizationRequired,
}

/// <summary>Bounded, secret-free diagnostics for the developer window and status UI.</summary>
public sealed record ProfileSyncStatus(
    ProfileSyncPhase Phase,
    int PendingEvents,
    long UploadedEvents,
    long PermanentlyRejectedEvents,
    long OutboxOverflowRejections,
    int ConsecutiveFailures,
    DateTimeOffset? LastUploadAt,
    DateTimeOffset? RetryAt)
{
    public static readonly ProfileSyncStatus Initial = new(ProfileSyncPhase.NotLinked, 0, 0, 0, 0, 0, null, null);
}
