namespace IceCrow.ProfileSync.History;

public enum ProfileHistoryAppendResult
{
    Added,
    Duplicate,
    Ignored,
    Full,
}

/// <summary>
/// Bounded local archive independent from upload acknowledgement. A single
/// gate owns the in-memory index while <see cref="ProfileHistoryFile"/> owns
/// the untrusted JSON Lines boundary.
/// </summary>
public sealed class ProfileHistoryStore : IDisposable
{
    public const int MaximumItems = 4096;
    public const int MaximumFileBytes = 64 * 1024 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ProfileHistoryFile _file;
    private readonly int _maximumItems;
    private List<ProfileEvent>? _events;
    private HashSet<Guid>? _ids;
    private HashSet<ProfileMatchKey>? _matchKeys;
    private bool _requiresRewrite;

    public ProfileHistoryStore(string path, int maximumItems = MaximumItems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumItems is < 1 or > MaximumItems)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        }

        _file = new ProfileHistoryFile(path);
        _maximumItems = maximumItems;
    }

    public bool RecoveredTruncatedTail { get; private set; }

    public async Task<ProfileHistorySnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var events = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return ProfileHistoryProjection.Create(events, RecoveredTruncatedTail);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProfileHistoryAppendResult> AppendAsync(
        ProfileEvent profileEvent,
        CancellationToken cancellationToken = default)
    {
        ProfileEvent.Validate(profileEvent);
        if (!ProfileHistoryProjection.IsHistoricalType(profileEvent.Type))
        {
            return ProfileHistoryAppendResult.Ignored;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var events = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            if (_ids!.Contains(profileEvent.EventId) || IsDuplicateMatch(profileEvent))
            {
                return ProfileHistoryAppendResult.Duplicate;
            }

            if (events.Count >= _maximumItems)
            {
                return ProfileHistoryAppendResult.Full;
            }

            var rewroteHistory = _requiresRewrite;
            var written = rewroteHistory
                ? await _file.RewriteAsync(events.Append(profileEvent), cancellationToken).ConfigureAwait(false)
                : await _file.TryAppendAsync(profileEvent, cancellationToken).ConfigureAwait(false);
            if (!written)
            {
                return ProfileHistoryAppendResult.Full;
            }

            events.Add(profileEvent);
            _ids.Add(profileEvent.EventId);
            AddMatchKey(profileEvent);
            _requiresRewrite = false;
            if (rewroteHistory)
            {
                RecoveredTruncatedTail = false;
            }

            return ProfileHistoryAppendResult.Added;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<List<ProfileEvent>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (_events is not null)
        {
            return _events;
        }

        var replay = await _file.ReplayAsync(_maximumItems, cancellationToken).ConfigureAwait(false);
        var duplicateMatches = ProfileMatchIdentity.CollapseDuplicateMatches(replay.Events);
        _events = replay.Events;
        _ids = replay.Events.Select(static item => item.EventId).ToHashSet();
        _matchKeys = MatchKeys(replay.Events);
        _requiresRewrite = replay.RequiresRewrite || duplicateMatches > 0;
        RecoveredTruncatedTail = replay.RecoveredTruncatedTail;
        if (duplicateMatches > 0 && !replay.RequiresRewrite)
        {
            if (!await _file.RewriteAsync(replay.Events, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("The deduplicated local match history exceeds its size limit.");
            }

            _requiresRewrite = false;
        }

        return _events;
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
