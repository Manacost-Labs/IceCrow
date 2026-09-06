using System.Collections.ObjectModel;
using IceCrow.Hearthstone.ClientState;

namespace IceCrow.ProfileSync.Arena;

/// <summary>
/// Deterministic state machine over <see cref="ArenaClientSnapshot"/>
/// observations. It returns the profile events proven by each snapshot (draft
/// picks and run records) and never enqueues or performs IO itself; the caller
/// owns the outbox. Client state is authoritative for the draft, the final
/// deck, the score, completion, and the rating, so nothing is inferred from
/// thresholds such as twelve wins or three losses. Absent client state emits
/// nothing and keeps the open run so a transient read failure loses no data.
/// </summary>
public sealed class ArenaRunCollector
{
    private readonly Lock _sync = new();
    private ArenaRunState? _run;
    private ArenaRunCollectorStatus _status = ArenaRunCollectorStatus.Initial;

    public ArenaRunCollectorStatus Status => Volatile.Read(ref _status);

    public IReadOnlyList<ProfileEvent> Observe(ArenaClientSnapshot? snapshot)
    {
        lock (_sync)
        {
            if (snapshot is null)
            {
                Publish(ClientStateProviderStatus.Unavailable, Status.ObservedSnapshots, Status.EmittedEvents, Status.RunsStarted);
                return ReadOnlyCollection<ProfileEvent>.Empty;
            }

            var runsStarted = Status.RunsStarted;
            if (ShouldStartRun(snapshot))
            {
                _run = new ArenaRunState(Guid.CreateVersion7(), snapshot);
                runsStarted++;
            }

            var events = _run is null ? ReadOnlyCollection<ProfileEvent>.Empty : Collect(_run, snapshot);
            Publish(ClientStateProviderStatus.Connected, Status.ObservedSnapshots + 1, Status.EmittedEvents + events.Count, runsStarted);
            return events;
        }
    }

    /// <summary>Associates a Power.log match window with the open run; see <see cref="ArenaMatchAssociation"/>.</summary>
    public ArenaMatchAssociation Associate(DateTimeOffset matchStartedAt, DateTimeOffset matchEndedAt)
    {
        if (matchEndedAt < matchStartedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(matchEndedAt), "A match cannot end before it started.");
        }

        lock (_sync)
        {
            return _run?.Associate(matchStartedAt, matchEndedAt) ?? ArenaMatchAssociation.Unknown;
        }
    }

    private bool ShouldStartRun(ArenaClientSnapshot snapshot)
    {
        if (!snapshot.IsDrafting)
        {
            return false;
        }

        if (_run is null)
        {
            return true;
        }

        if (snapshot.RunKey is not null && _run.RunKey is not null)
        {
            return !string.Equals(snapshot.RunKey, _run.RunKey, StringComparison.Ordinal);
        }

        return _run.Draft.HasPrevious && snapshot.DeckCardIds.Count < _run.Draft.LastDeckCount;
    }

    private static ReadOnlyCollection<ProfileEvent> Collect(ArenaRunState run, ArenaClientSnapshot snapshot)
    {
        var events = new List<ProfileEvent>(2);
        if (run.ObservePick(snapshot) is { } pick)
        {
            events.Add(ProfileEvent.Create(
                ProfileEventType.ArenaDraftPick,
                snapshot.ObservedAt,
                pick,
                ArenaEventIds.ForDraftPick(pick.RunId, pick.PickIndex)));
        }

        if (run.ObserveRun(snapshot) is { } record)
        {
            events.Add(ProfileEvent.Create(
                ProfileEventType.ArenaRun,
                snapshot.ObservedAt,
                record,
                ArenaEventIds.ForRunRecord(record.RunId, record.Wins, record.Losses, record.IsComplete)));
        }

        return events.Count == 0 ? ReadOnlyCollection<ProfileEvent>.Empty : events.AsReadOnly();
    }

    private void Publish(ClientStateProviderStatus availability, long observed, long emitted, long runsStarted)
    {
        var status = new ArenaRunCollectorStatus(
            availability,
            _run?.RunId,
            _run?.IsDrafting ?? false,
            _run?.Draft.PickCount ?? 0,
            _run?.Draft.GapCount ?? 0,
            observed,
            emitted,
            runsStarted);
        Volatile.Write(ref _status, status);
    }
}
