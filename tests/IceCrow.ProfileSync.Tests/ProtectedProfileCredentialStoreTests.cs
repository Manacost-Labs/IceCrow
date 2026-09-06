using System.Security.Cryptography;

namespace IceCrow.ProfileSync.Tests;

public sealed class ProtectedProfileCredentialStoreTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "icecrow-credential-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task RoundTripsThroughTheProtectorAndNeverStoresPlaintext()
    {
        using var store = Create();
        var credential = Credential("https://hearthpulse.net");

        await store.SaveAsync(credential);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(credential.ServerOrigin, loaded.ServerOrigin);
        Assert.Equal(credential.AccessToken, loaded.AccessToken);
        Assert.Equal(credential.RefreshToken, loaded.RefreshToken);
        Assert.Equal(credential.AccessTokenExpiresAt, loaded.AccessTokenExpiresAt);
        Assert.Equal(credential.LinkedAt, loaded.LinkedAt);
        Assert.Equal(credential.Scopes, loaded.Scopes);
        var raw = await File.ReadAllTextAsync(Path.Combine(_directory, "credential.bin"));
        Assert.DoesNotContain(credential.AccessToken, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(credential.RefreshToken, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TamperedFileIsRejectedAsInvalidData()
    {
        using var store = Create();
        await store.SaveAsync(Credential("https://hearthpulse.net"));
        var path = Path.Combine(_directory, "credential.bin");
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(path, bytes);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
    }

    [Fact]
    public async Task ClearRemovesTheCredentialAndMissingFileIsNull()
    {
        using var store = Create();
        Assert.Null(await store.LoadAsync());
        await store.SaveAsync(Credential("https://hearthpulse.net"));

        await store.ClearAsync();

        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public void CredentialNeverPrintsItsTokens()
    {
        var credential = Credential("https://hearthpulse.net");

        var printed = credential.ToString();

        Assert.DoesNotContain(credential.AccessToken, printed, StringComparison.Ordinal);
        Assert.DoesNotContain(credential.RefreshToken, printed, StringComparison.Ordinal);
        Assert.Contains("[redacted]", printed, StringComparison.Ordinal);
        Assert.Contains("tracker.write", printed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyHttpsOriginsAreAccepted()
    {
        using var store = Create();

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(Credential("http://hearthpulse.net")));
    }

    private ProtectedProfileCredentialStore Create() =>
        new(Path.Combine(_directory, "credential.bin"), new ChecksumProtector());

    private static ProfileCredential Credential(string origin) => new(
        origin,
        "mca_access_secret",
        Timestamp.AddMinutes(15),
        "mca_refresh_secret",
        ["profile.read", "tracker.write"],
        Timestamp);

    /// <summary>XOR obfuscation plus a hash trailer: enough to prove tamper detection paths.</summary>
    private sealed class ChecksumProtector : ISecretProtector
    {
        private const byte Key = 0x5A;

        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            var body = plaintext.ToArray();
            for (var index = 0; index < body.Length; index++)
            {
                body[index] ^= Key;
            }

            return [.. body, .. SHA256.HashData(plaintext)];
        }

        public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
        {
            if (protectedData.Length < 32)
            {
                throw new CryptographicException("Too short.");
            }

            var body = protectedData[..^32].ToArray();
            for (var index = 0; index < body.Length; index++)
            {
                body[index] ^= Key;
            }

            return SHA256.HashData(body).AsSpan().SequenceEqual(protectedData[^32..])
                ? body
                : throw new CryptographicException("Checksum mismatch.");
        }
    }
}
