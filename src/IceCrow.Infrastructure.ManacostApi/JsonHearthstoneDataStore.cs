using System.Security.Cryptography;
using System.Text.Json;
using IceCrow.Hearthstone.Data;

namespace IceCrow.Infrastructure.ManacostApi;

public sealed class JsonHearthstoneDataStore : IHearthstoneDataStore
{
    public const int CurrentSchemaVersion = 1;
    private const int MaximumCacheBytes = 64 * 1024 * 1024;
    private readonly string _cachePath;

    public JsonHearthstoneDataStore(string cachePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        _cachePath = Path.GetFullPath(cachePath);
    }

    public async Task<HearthstoneDataSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_cachePath))
        {
            return null;
        }

        return await LoadAndValidateAsync(_cachePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(HearthstoneDataSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);

        var directory = Path.GetDirectoryName(_cachePath)
            ?? throw new InvalidOperationException("The cache path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_cachePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var envelope = CacheEnvelope.FromSnapshot(snapshot);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, envelope, SnapshotCodec.JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            _ = await LoadAndValidateAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _cachePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<HearthstoneDataSnapshot> LoadAndValidateAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaximumCacheBytes)
        {
            throw new InvalidDataException("The Hearthstone data cache has an invalid size.");
        }

        CacheEnvelope? envelope;
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            envelope = await JsonSerializer.DeserializeAsync<CacheEnvelope>(
                stream,
                SnapshotCodec.JsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Hearthstone data cache contains invalid JSON.", exception);
        }

        if (envelope is null || envelope.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException("The Hearthstone data cache schema is unsupported.");
        }

        var snapshot = envelope.ToSnapshot();
        ValidateSnapshot(snapshot);
        return snapshot;
    }

    private static void ValidateSnapshot(HearthstoneDataSnapshot snapshot)
    {
        if (snapshot.Version.SchemaVersion != CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(snapshot.Version.DataVersion) ||
            snapshot.Cards.Count > 20_000 ||
            snapshot.Heroes.Count > 1_000)
        {
            throw new InvalidDataException("The Hearthstone data cache metadata is invalid.");
        }

        var actualHash = SnapshotCodec.ComputeContentHash(
            snapshot.Cards,
            snapshot.Heroes,
            snapshot.Version.DataVersion,
            snapshot.Version.HearthstoneBuild);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHash),
                ParseHash(snapshot.Version.Sha256)))
        {
            throw new InvalidDataException("The Hearthstone data cache hash does not match its content.");
        }

        try
        {
            _ = snapshot.Cards.ToDictionary(card => card.CardId, StringComparer.OrdinalIgnoreCase);
            _ = snapshot.Cards.ToDictionary(card => card.DbfId);
            _ = snapshot.Heroes.ToDictionary(hero => hero.CardId, StringComparer.OrdinalIgnoreCase);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The Hearthstone data cache contains duplicate identities.", exception);
        }
    }

    private static byte[] ParseHash(string value)
    {
        if (value.Length != 64)
        {
            throw new InvalidDataException("The Hearthstone data cache hash is invalid.");
        }

        try
        {
            return Convert.FromHexString(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The Hearthstone data cache hash is invalid.", exception);
        }
    }

    private sealed record CacheEnvelope(
        int SchemaVersion,
        string DataVersion,
        string? HearthstoneBuild,
        string Sha256,
        DateTimeOffset CreatedAt,
        CardDefinition[] Cards,
        BattlegroundsHeroDefinition[] Heroes)
    {
        public static CacheEnvelope FromSnapshot(HearthstoneDataSnapshot snapshot) => new(
            snapshot.Version.SchemaVersion,
            snapshot.Version.DataVersion,
            snapshot.Version.HearthstoneBuild,
            snapshot.Version.Sha256,
            snapshot.Version.CreatedAt,
            snapshot.Cards.ToArray(),
            snapshot.Heroes.ToArray());

        public HearthstoneDataSnapshot ToSnapshot() => new(
            new HearthstoneDataVersion(SchemaVersion, DataVersion, HearthstoneBuild, Sha256, CreatedAt),
            Cards,
            Heroes);
    }
}
