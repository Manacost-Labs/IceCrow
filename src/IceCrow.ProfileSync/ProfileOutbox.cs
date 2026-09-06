using System.Text.Json;

namespace IceCrow.ProfileSync;

/// <summary>
/// Durable, bounded, idempotent local queue of profile events. One JSON array
/// file written atomically (temp file + move) behind a single gate.
/// </summary>
public sealed class ProfileOutbox : IDisposable
{
    public const int MaximumHistoryItems = 256;
    public const int MaximumBatchSize = 50;
    public const int MaximumFileBytes = 16 * 1024 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public ProfileOutbox(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false)).Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProfileOutboxResult> EnqueueAsync(
        ProfileEvent profileEvent,
        CancellationToken cancellationToken = default)
    {
        ProfileEvent.Validate(profileEvent);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            if (items.Any(item => item.EventId == profileEvent.EventId))
            {
                return ProfileOutboxResult.Duplicate;
            }

            var result = ProfileOutboxResult.Enqueued;
            if (ProfileEventType.IsLatestOnly(profileEvent.Type))
            {
                var replaced = items.RemoveAll(item =>
                    string.Equals(item.Type, profileEvent.Type, StringComparison.Ordinal));
                result = replaced > 0 ? ProfileOutboxResult.Replaced : result;
            }
            else if (items.Count(item => !ProfileEventType.IsLatestOnly(item.Type)) >= MaximumHistoryItems)
            {
                return ProfileOutboxResult.Full;
            }

            items.Add(profileEvent);
            await SaveUnsafeAsync(items, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ProfileEvent>> PeekBatchAsync(
        int maximumItems,
        CancellationToken cancellationToken = default)
    {
        if (maximumItems is < 1 or > MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false))
                .Take(maximumItems)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Removes acknowledged or permanently rejected events by id.</summary>
    public async Task<int> RemoveAsync(
        IEnumerable<Guid> eventIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventIds);
        var removable = eventIds.Take(MaximumBatchSize * 2).ToHashSet();
        if (removable.Count == 0)
        {
            return 0;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var removed = items.RemoveAll(item => removable.Contains(item.EventId));
            if (removed > 0)
            {
                await SaveUnsafeAsync(items, cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<ProfileEvent>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var info = new FileInfo(_path);
        if (info.Length is <= 0 or > MaximumFileBytes)
        {
            throw new InvalidDataException("The profile outbox has an invalid size.");
        }

        try
        {
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var items = new List<ProfileEvent>();
            await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<ProfileEvent>(
                               stream,
                               ProfileJson.Options,
                               cancellationToken).ConfigureAwait(false))
            {
                if (item is null)
                {
                    throw new InvalidDataException("The profile outbox contains a null item.");
                }

                ProfileEvent.Validate(item);
                if (items.Count > MaximumHistoryItems)
                {
                    throw new InvalidDataException("The profile outbox item limit was exceeded.");
                }

                items.Add(item);
            }

            return items;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The profile outbox contains invalid JSON.", exception);
        }
    }

    private async Task SaveUnsafeAsync(List<ProfileEvent> items, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The outbox path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, items, ProfileJson.Options, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (stream.Length > MaximumFileBytes)
                {
                    throw new InvalidDataException("The profile outbox exceeded its storage limit.");
                }
            }

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
}
