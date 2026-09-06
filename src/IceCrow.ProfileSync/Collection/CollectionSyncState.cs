using System.Text.Json;

namespace IceCrow.ProfileSync.Collection;

/// <summary>What was last enqueued, so an unchanged collection is never re-sent after a restart.</summary>
public sealed record CollectionSyncState(
    string ContentHash,
    DateTimeOffset ObservedAt,
    CollectionRefreshTrigger Trigger,
    int CardCount,
    DateTimeOffset EnqueuedAt);

/// <summary>
/// Tiny JSON file next to the outbox, written atomically. It is a dedupe
/// cache, not history: an unreadable file reads as "never synced", which costs
/// at most one redundant snapshot upload and never blocks syncing.
/// </summary>
public sealed class CollectionSyncStateFile
{
    public const int MaximumFileBytes = 4 * 1024;

    private readonly string _path;

    public CollectionSyncStateFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<CollectionSyncState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var info = new FileInfo(_path);
        if (info.Length is <= 0 or > MaximumFileBytes)
        {
            return null;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize<CollectionSyncState>(bytes, ProfileJson.Options);
            return IsValid(state) ? state : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(CollectionSyncState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!IsValid(state))
        {
            throw new InvalidDataException("The collection sync state is outside its contract limits.");
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, ProfileJson.Options);
        if (bytes.Length > MaximumFileBytes)
        {
            throw new InvalidDataException("The collection sync state exceeded its storage limit.");
        }

        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The state path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
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

    private static bool IsValid(CollectionSyncState? state) =>
        state is not null &&
        !string.IsNullOrWhiteSpace(state.ContentHash) &&
        state.ContentHash.Length == 64 &&
        state.CardCount >= 0 &&
        Enum.IsDefined(state.Trigger);
}
