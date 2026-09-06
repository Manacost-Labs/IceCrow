namespace IceCrow.ProfileSync;

public enum ProfileUploadStatus
{
    /// <summary>The server processed the batch; see the acknowledged and rejected ids.</summary>
    Accepted,

    /// <summary>The device credential is missing, expired, or revoked; re-linking is required.</summary>
    Unauthorized,

    /// <summary>The server asked the client to slow down.</summary>
    RateLimited,

    /// <summary>Network or server failure; the batch stays queued and is retried.</summary>
    Unavailable,
}

/// <summary>
/// Outcome of one batch upload. Permanently rejected events (schema or
/// validation failures the server will never accept) are removed from the
/// outbox and counted; everything else is retried.
/// </summary>
public sealed record ProfileUploadResult(
    ProfileUploadStatus Status,
    IReadOnlyList<Guid> AcknowledgedEventIds,
    IReadOnlyList<Guid> PermanentlyRejectedEventIds,
    TimeSpan? RetryAfter = null)
{
    public static ProfileUploadResult Unavailable(TimeSpan? retryAfter = null) =>
        new(ProfileUploadStatus.Unavailable, [], [], retryAfter);

    public static ProfileUploadResult Unauthorized() =>
        new(ProfileUploadStatus.Unauthorized, [], []);

    public static ProfileUploadResult RateLimited(TimeSpan? retryAfter) =>
        new(ProfileUploadStatus.RateLimited, [], [], retryAfter);
}
