namespace IceCrow.ProfileSync;

/// <summary>
/// Upload pacing. Defaults favour zero gameplay impact: small batches,
/// minutes-scale idle polling, exponential backoff with jitter, and a hold
/// while a match is in progress.
/// </summary>
public sealed record ProfileSyncOptions(
    int BatchSize = 25,
    TimeSpan? IdleInterval = null,
    TimeSpan? MinimumBackoff = null,
    TimeSpan? MaximumBackoff = null,
    bool HoldDuringGameplay = true)
{
    public static readonly ProfileSyncOptions Default = new();

    public TimeSpan EffectiveIdleInterval => IdleInterval ?? TimeSpan.FromMinutes(5);

    public TimeSpan EffectiveMinimumBackoff => MinimumBackoff ?? TimeSpan.FromSeconds(30);

    public TimeSpan EffectiveMaximumBackoff => MaximumBackoff ?? TimeSpan.FromMinutes(30);

    public void Validate()
    {
        if (BatchSize is < 1 or > ProfileOutbox.MaximumBatchSize ||
            EffectiveIdleInterval <= TimeSpan.Zero ||
            EffectiveMinimumBackoff <= TimeSpan.Zero ||
            EffectiveMaximumBackoff < EffectiveMinimumBackoff)
        {
            throw new ArgumentOutOfRangeException(nameof(ProfileSyncOptions), "Profile sync options are invalid.");
        }
    }
}
