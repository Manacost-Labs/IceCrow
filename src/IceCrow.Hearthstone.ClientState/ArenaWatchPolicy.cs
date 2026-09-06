namespace IceCrow.Hearthstone.ClientState;

/// <summary>
/// Pure polling policy for the Arena client watcher. The watcher only runs
/// while a draft is visibly in progress; outside a draft it sleeps and must be
/// woken by an explicit trigger (scene change, manual refresh, match end)
/// rather than by a timer. Absent client state is never a reason to poll.
/// </summary>
public sealed class ArenaWatchPolicy
{
    public static readonly TimeSpan MinimumDraftPollingInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan MaximumDraftPollingInterval = TimeSpan.FromMilliseconds(1000);
    public static readonly TimeSpan DraftPollingInterval = TimeSpan.FromMilliseconds(750);

    public static readonly ArenaWatchPolicy Default = new(DraftPollingInterval);

    public ArenaWatchPolicy(TimeSpan draftPollingInterval)
    {
        EffectiveDraftPollingInterval = Clamp(draftPollingInterval);
    }

    /// <summary>The configured interval after clamping into the allowed draft budget.</summary>
    public TimeSpan EffectiveDraftPollingInterval { get; }

    /// <summary>Null means the watcher sleeps; otherwise the delay before the next read.</summary>
    public TimeSpan? NextInterval(ArenaClientSnapshot? last) =>
        last is { IsDrafting: true } ? EffectiveDraftPollingInterval : null;

    public static TimeSpan Clamp(TimeSpan interval)
    {
        if (interval < MinimumDraftPollingInterval)
        {
            return MinimumDraftPollingInterval;
        }

        return interval > MaximumDraftPollingInterval ? MaximumDraftPollingInterval : interval;
    }
}
