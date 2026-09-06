namespace IceCrow.ProfileSync;

/// <summary>
/// Authenticated batch upload. Implementations must never throw for expected
/// network or HTTP failures; they translate them into a result status.
/// </summary>
public interface IProfileSyncTransport
{
    Task<ProfileUploadResult> UploadAsync(
        IReadOnlyList<ProfileEvent> events,
        CancellationToken cancellationToken);
}
