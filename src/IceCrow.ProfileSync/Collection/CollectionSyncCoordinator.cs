using IceCrow.Hearthstone.ClientState;
using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync.Collection;

/// <summary>
/// Reads the owned collection once per explicit trigger, normalizes it, and
/// queues a <see cref="CollectionSnapshotRecord"/> only when its content hash
/// differs from the last enqueued one. The outbox keeps only the newest
/// pending snapshot. Provider failures are isolated and reported as
/// <see cref="CollectionSyncOutcome.Unavailable"/>; the card list is never
/// logged or exposed through the status.
/// </summary>
public sealed class CollectionSyncCoordinator : IDisposable
{
    private readonly ClientStateSourceGuard<CollectionSnapshot> _source;
    private readonly ProfileOutbox _outbox;
    private readonly CollectionSyncStateFile _stateFile;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CollectionSyncStatus _status = CollectionSyncStatus.Initial;
    private CollectionSyncState? _state;
    private bool _stateLoaded;

    public CollectionSyncCoordinator(
        ICollectionSource source,
        ProfileOutbox outbox,
        string statePath,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(outbox);
        _source = new ClientStateSourceGuard<CollectionSnapshot>(source);
        _outbox = outbox;
        _stateFile = new CollectionSyncStateFile(statePath);
        _time = timeProvider ?? TimeProvider.System;
    }

    public CollectionSyncStatus Status => Volatile.Read(ref _status);

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<CollectionSyncOutcome> RefreshAsync(
        CollectionRefreshTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStateLoadedAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = await _source.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return Publish(trigger, CollectionSyncOutcome.Unavailable);
            }

            var cards = CollectionContentHash.Normalize(snapshot);
            var contentHash = CollectionContentHash.Compute(cards);
            if (string.Equals(_state?.ContentHash, contentHash, StringComparison.Ordinal))
            {
                return Publish(trigger, CollectionSyncOutcome.Unchanged);
            }

            var outcome = await EnqueueAsync(snapshot.ObservedAt, contentHash, cards, cancellationToken).ConfigureAwait(false);
            if (outcome is CollectionSyncOutcome.Rejected)
            {
                return Publish(trigger, outcome);
            }

            var state = new CollectionSyncState(contentHash, snapshot.ObservedAt, trigger, cards.Count, _time.GetUtcNow());
            await _stateFile.SaveAsync(state, cancellationToken).ConfigureAwait(false);
            _state = state;
            return Publish(trigger, outcome);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CollectionSyncOutcome> EnqueueAsync(
        DateTimeOffset observedAt,
        string contentHash,
        IReadOnlyList<CollectionCardRecord> cards,
        CancellationToken cancellationToken)
    {
        ProfileEvent profileEvent;
        try
        {
            profileEvent = ProfileEvent.Create(
                ProfileEventType.CollectionSnapshot,
                observedAt,
                new CollectionSnapshotRecord(observedAt, contentHash, cards));
        }
        catch (InvalidDataException)
        {
            return CollectionSyncOutcome.Rejected;
        }

        return await _outbox.EnqueueAsync(profileEvent, cancellationToken).ConfigureAwait(false) switch
        {
            ProfileOutboxResult.Enqueued => CollectionSyncOutcome.Enqueued,
            ProfileOutboxResult.Replaced => CollectionSyncOutcome.Replaced,
            _ => CollectionSyncOutcome.Rejected,
        };
    }

    private async Task EnsureStateLoadedAsync(CancellationToken cancellationToken)
    {
        if (_stateLoaded)
        {
            return;
        }

        _state = await _stateFile.LoadAsync(cancellationToken).ConfigureAwait(false);
        _stateLoaded = true;
    }

    private CollectionSyncOutcome Publish(CollectionRefreshTrigger trigger, CollectionSyncOutcome outcome)
    {
        var previous = Status;
        var status = new CollectionSyncStatus(
            trigger,
            outcome,
            _time.GetUtcNow(),
            CollectionSyncStatus.PrefixOf(_state?.ContentHash),
            _state?.ObservedAt,
            _state?.CardCount ?? 0,
            previous.RejectedSnapshots + (outcome is CollectionSyncOutcome.Rejected ? 1 : 0),
            _source.FailureCount);
        Volatile.Write(ref _status, status);
        return outcome;
    }
}
