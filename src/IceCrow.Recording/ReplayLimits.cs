namespace IceCrow.Recording;

/// <summary>
/// Work budgets for one replay. Structural caps (entities, lobby players,
/// board minions, snapshot count) stay fixed constants on
/// <see cref="ReplayRunner"/>; these budgets bound the work a hostile or
/// runaway recording can charge and are configurable for boundary tests.
/// </summary>
public sealed record ReplayLimits(
    long MaximumSnapshotWorkUnits = ReplayLimits.DefaultMaximumSnapshotWorkUnits,
    long MaximumEventSnapshotWorkUnits = ReplayLimits.DefaultMaximumEventSnapshotWorkUnits,
    long MaximumStateMaterializationWorkUnits = ReplayLimits.DefaultMaximumStateMaterializationWorkUnits,
    long MaximumTimelineWorkUnits = ReplayLimits.DefaultMaximumTimelineWorkUnits)
{
    public const long DefaultMaximumSnapshotWorkUnits = 1_000_000;

    // Calibrated 2026-08-31 against four consecutive real full-match captures
    // (154,610 / 205,922 / 213,253 / 174,684 events). Every applied event that
    // touches an entity materializes a real FrozenDictionary snapshot of its
    // tags, so the charge of 1 + tag count per event is honest work; the four
    // matches measured a stable 10.03-10.23 units/event (maximum total
    // 2,180,808; maximum tags on one entity 42 of the 256 cap). The budget is
    // the supported MaximumEventCount (250,000) x 16 units/event — the
    // measured bound with a 1.5x safety factor rounded up — which replays
    // every real full match with ~1.8x total headroom while staying far below
    // the theoretical writer-acceptable ceiling of MaximumEventCount x
    // (1 + 256 tags) ~= 64M, so adversarial tag-stuffed streams still fail
    // fast (~2-3 s of bounded work at measured replay throughput).
    public const long DefaultMaximumEventSnapshotWorkUnits = 4_000_000;

    public const long DefaultMaximumStateMaterializationWorkUnits = 10_000_000;

    // Real-match calibrated (F7): observed maximum 213,502 timeline units for
    // a full match, ~4.7x headroom.
    public const long DefaultMaximumTimelineWorkUnits = 1_000_000;

    public static ReplayLimits Default { get; } = new();

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumSnapshotWorkUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEventSnapshotWorkUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumStateMaterializationWorkUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumTimelineWorkUnits);
    }
}
