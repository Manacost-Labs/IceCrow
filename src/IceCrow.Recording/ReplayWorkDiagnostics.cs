namespace IceCrow.Recording;

/// <summary>
/// Privacy-safe work counters for one replay: how much bounded work the
/// runner actually charged against each <see cref="ReplayLimits"/> budget.
/// Contains only counts — no entity names, card ids, or raw content — so it
/// may appear in developer tooling output and committed calibration notes.
/// </summary>
public sealed record ReplayWorkDiagnostics(
    int ProcessedEventCount,
    long EventSnapshotWorkUnits,
    long TimelineWorkUnits,
    long BoardSnapshotWorkUnits,
    long StateMaterializationWorkUnits,
    int EntityCount,
    int MaximumTagsOnEntity);
