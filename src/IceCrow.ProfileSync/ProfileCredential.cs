namespace IceCrow.ProfileSync;

/// <summary>
/// The revocable device credential issued by HearthPulse device linking.
/// Tokens are secrets: never log, display, or serialize them outside the
/// protected credential store.
/// </summary>
public sealed record ProfileCredential(
    string ServerOrigin,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    IReadOnlyList<string> Scopes,
    DateTimeOffset LinkedAt)
{
    public const int MaximumTokenLength = 512;
    public const int MaximumScopes = 16;

    public bool IsAccessTokenExpired(DateTimeOffset now) => AccessTokenExpiresAt <= now;
}

public interface IProfileCredentialStore
{
    Task<ProfileCredential?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ProfileCredential credential, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Platform secret protection (Windows DPAPI in production). The protector
/// sees opaque bytes only; the adapter implementation lives in the platform
/// boundary and never references profile types.
/// </summary>
public interface ISecretProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}
