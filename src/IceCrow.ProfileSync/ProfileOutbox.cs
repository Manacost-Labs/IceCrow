using IceCrow.ProfileSync.Outbox;

namespace IceCrow.ProfileSync;

/// <summary>
/// Durable, bounded, idempotent local queue of profile events. History
/// events live in an append-only journal (one flushed line per event, no
/// rewrite per gameplay event); the latest-only collection snapshot lives in
/// its own small file. The state is loaded once and kept in memory behind a
/// single gate.
/// </summary>
public sealed class ProfileOutbox : IDisposable
{
    public const int MaximumHistoryItems = 4096;
    public const int MaximumBatchSize = 50;
    public const int MaximumFileBytes = ProfileJournalFile.MaximumBytes;
    public const int CompactionTombstones = 256;
    private const string JournalFileName = "outbox.jsonl";
    private const string CollectionFileName = "collection-pending.json";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _maximumHistoryItems;
    private readonly string _directory;
    private readonly string _legacyPath;
    private readonly ProfileJournalFile _journal;
    private readonly ProfileCollectionFile _collectionFile;
    private List<ProfileEvent>? _history;
    private HashSet<Guid>? _ids;
    private HashSet<ProfileMatchKey>? _matchKeys;
    private ProfileEvent? _collection;
    private int _tombstones;

    /// <param name="path">
    /// The legacy single-file outbox path; the journal and the collection file
    /// live in the same directory and a legacy file is imported once.
    /// </param>
    /// <param name="maximumHistoryItems">Bound for history events; tests lower it, production keeps the default.</param>
    public ProfileOutbox(string path, int maximumHistoryItems = MaximumHistoryItems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumHistoryItems is < 1 or > MaximumHistoryItems)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumHistoryItems));
        }

        _maximumHistoryItems = maximumHistoryItems;
        _legacyPath = Path.GetFullPath(path);
        _directory = Path.GetDirectoryName(_legacyPath)
            ?? throw new ArgumentException("The outbox path has no parent directory.", nameof(path));
        _journal = new ProfileJournalFile(Path.Combine(_directory, JournalFileName));
        _collectionFile = new ProfileCollectionFile(Path.Combine(_directory, CollectionFileName));
    }

    /// <summary>Set when a crash left an incomplete journal line that was dropped on load.</summary>
    public bool TruncatedTailRecovered { get; private set; }

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
            var history = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return history.Count + (_collection is null ? 0 : 1);
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
            var history = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            if (_ids!.Contains(profileEvent.EventId) ||
                _collection?.EventId == profileEvent.EventId ||
                IsDuplicateMatch(profileEvent))
            {
                return ProfileOutboxResult.Duplicate;
            }

            if (ProfileEventType.IsLatestOnly(profileEvent.Type))
            {
                var replaced = _collection is not null;
                await _collectionFile.WriteAsync(profileEvent, cancellationToken).ConfigureAwait(false);
                _collection = profileEvent;
                return replaced ? ProfileOutboxResult.Replaced : ProfileOutboxResult.Enqueued;
            }

            if (history.Count >= _maximumHistoryItems)
            {
                return ProfileOutboxResult.Full;
            }

            if (_journal.Length > MaximumFileBytes / 2 && _tombstones > 0)
            {
                await CompactUnsafeAsync(history, cancellationToken).ConfigureAwait(false);
            }

            if (_journal.Length >= MaximumFileBytes)
            {
                return ProfileOutboxResult.Full;
            }

            if (!await _journal.TryAppendEventAsync(profileEvent, cancellationToken).ConfigureAwait(false))
            {
                return ProfileOutboxResult.Full;
            }

            history.Add(profileEvent);
            _ids.Add(profileEvent.EventId);
            AddMatchKey(profileEvent);
            return ProfileOutboxResult.Enqueued;
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
            var history = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var batch = history.Take(maximumItems).ToList();
            if (_collection is { } collection && batch.Count < maximumItems)
            {
                batch.Add(collection);
            }

            return batch;
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
            var history = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var removed = 0;
            if (_collection is { } collection && removable.Remove(collection.EventId))
            {
                _collectionFile.Delete();
                _collection = null;
                removed++;
            }

            var acknowledged = history.Where(item => removable.Contains(item.EventId)).Select(static item => item.EventId).ToArray();
            if (acknowledged.Length == 0)
            {
                return removed;
            }

            if (!await _journal.TryAppendAcksAsync(acknowledged, cancellationToken).ConfigureAwait(false))
            {
                var remaining = history.Where(item => !removable.Contains(item.EventId)).ToList();
                await CompactUnsafeAsync(remaining, cancellationToken).ConfigureAwait(false);
                history.Clear();
                history.AddRange(remaining);
                _ids!.ExceptWith(acknowledged);
                _matchKeys = MatchKeys(history);
                return removed + acknowledged.Length;
            }

            history.RemoveAll(item => removable.Contains(item.EventId));
            _ids!.ExceptWith(acknowledged);
            _matchKeys = MatchKeys(history);
            _tombstones += acknowledged.Length;
            removed += acknowledged.Length;
            if (_tombstones >= CompactionTombstones)
            {
                await CompactUnsafeAsync(history, cancellationToken).ConfigureAwait(false);
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
        if (_history is not null)
        {
            return _history;
        }

        var replay = await _journal.ReplayAsync(
            _maximumHistoryItems,
            checked(_maximumHistoryItems + CompactionTombstones),
            cancellationToken).ConfigureAwait(false);
        TruncatedTailRecovered = replay.TruncatedTailRecovered;
        _history = replay.Events;
        var duplicateMatches = ProfileMatchIdentity.CollapseDuplicateMatches(_history);
        _ids = replay.Events.Select(static item => item.EventId).ToHashSet();
        _matchKeys = MatchKeys(replay.Events);
        _tombstones = replay.Tombstones;
        _collection = await _collectionFile.ReadAsync(cancellationToken).ConfigureAwait(false);
        await ImportLegacyUnsafeAsync(_history, cancellationToken).ConfigureAwait(false);
        if (_tombstones >= CompactionTombstones || replay.TruncatedTailRecovered || duplicateMatches > 0)
        {
            await CompactUnsafeAsync(_history, cancellationToken).ConfigureAwait(false);
        }

        return _history;
    }

    private async Task CompactUnsafeAsync(List<ProfileEvent> history, CancellationToken cancellationToken)
    {
        await _journal.CompactAsync(history, cancellationToken).ConfigureAwait(false);
        _tombstones = 0;
    }

    /// <summary>
    /// A pre-journal single-file outbox is imported once so no pending
    /// history is lost across the upgrade; the legacy file is then renamed.
    /// </summary>
    private async Task ImportLegacyUnsafeAsync(
        List<ProfileEvent> history,
        CancellationToken cancellationToken)
    {
        var legacy = await LegacyProfileOutboxFile.ReadAsync(
            _legacyPath,
            _maximumHistoryItems,
            cancellationToken).ConfigureAwait(false);
        if (legacy is null)
        {
            return;
        }

        foreach (var item in legacy.History)
        {
            if (_ids!.Contains(item.EventId) || IsDuplicateMatch(item))
            {
                continue;
            }

            if (history.Count >= _maximumHistoryItems ||
                !await _journal.TryAppendEventAsync(item, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("The legacy profile outbox exceeds the journal size limit.");
            }

            history.Add(item);
            _ids.Add(item.EventId);
            AddMatchKey(item);
        }

        if (legacy.Collection is { } collection && _collection?.EventId != collection.EventId)
        {
            await _collectionFile.WriteAsync(collection, cancellationToken).ConfigureAwait(false);
            _collection = collection;
        }

        LegacyProfileOutboxFile.MarkMigrated(_legacyPath);
    }

    private bool IsDuplicateMatch(ProfileEvent profileEvent) =>
        ProfileMatchIdentity.TryGetKey(profileEvent, out var key) && _matchKeys!.Contains(key);

    private void AddMatchKey(ProfileEvent profileEvent)
    {
        if (ProfileMatchIdentity.TryGetKey(profileEvent, out var key))
        {
            _matchKeys!.Add(key);
        }
    }

    private static HashSet<ProfileMatchKey> MatchKeys(IEnumerable<ProfileEvent> events) =>
        events
            .Select(static profileEvent => ProfileMatchIdentity.TryGetKey(profileEvent, out var key) ? key : (ProfileMatchKey?)null)
            .OfType<ProfileMatchKey>()
            .ToHashSet();

}
