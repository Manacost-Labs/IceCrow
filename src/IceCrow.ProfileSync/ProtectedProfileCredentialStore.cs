using System.Security.Cryptography;
using System.Text.Json;

namespace IceCrow.ProfileSync;

/// <summary>
/// Stores the device credential as protector-encrypted bytes on disk. A
/// tampered, oversized, or malformed file reads back as an explicit
/// <see cref="InvalidDataException"/>, never as a partially trusted token.
/// </summary>
public sealed class ProtectedProfileCredentialStore : IProfileCredentialStore, IDisposable
{
    public const int MaximumFileBytes = 16 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private readonly ISecretProtector _protector;

    public ProtectedProfileCredentialStore(string path, ISecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(protector);
        _path = Path.GetFullPath(path);
        _protector = protector;
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<ProfileCredential?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var info = new FileInfo(_path);
            if (info.Length is <= 0 or > MaximumFileBytes)
            {
                throw new InvalidDataException("The credential store has an invalid size.");
            }

            var protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
            return Decode(protectedBytes);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ProfileCredential credential, CancellationToken cancellationToken = default)
    {
        Validate(credential);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var protectedBytes = Encode(credential);
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("The credential path has no parent directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private byte[] Encode(ProfileCredential credential)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(credential, ProfileJson.Options);
        try
        {
            return _protector.Protect(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private ProfileCredential Decode(byte[] protectedBytes)
    {
        byte[] plaintext;
        try
        {
            plaintext = _protector.Unprotect(protectedBytes);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("The credential store could not be unprotected.", exception);
        }

        try
        {
            var credential = JsonSerializer.Deserialize<ProfileCredential>(plaintext, ProfileJson.Options)
                ?? throw new InvalidDataException("The credential store is empty.");
            Validate(credential);
            return credential;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The credential store contains invalid JSON.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static void Validate(ProfileCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (string.IsNullOrWhiteSpace(credential.ServerOrigin) ||
            !Uri.TryCreate(credential.ServerOrigin, UriKind.Absolute, out var origin) ||
            origin.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(credential.AccessToken) ||
            string.IsNullOrEmpty(credential.RefreshToken) ||
            credential.AccessToken.Length > ProfileCredential.MaximumTokenLength ||
            credential.RefreshToken.Length > ProfileCredential.MaximumTokenLength ||
            credential.Scopes is null ||
            credential.Scopes.Count > ProfileCredential.MaximumScopes)
        {
            throw new InvalidDataException("The profile credential is outside its contract limits.");
        }
    }
}
