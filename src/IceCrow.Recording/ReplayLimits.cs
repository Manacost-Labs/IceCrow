namespace IceCrow.Recording;

/// <summary>
/// Work-unit budgets for one replay. Structural caps (entities, lobby size,
/// board minions, snapshot count) stay fixed constants on
/// <see cref="ReplayRunner"/>; these budgets bound the work a hostile or
/// runaway recording can charge and are configurable for boundary tests.
/// </summary>
public sealed record ReplayLimits(
    long MaximumSnapshotWorkUnits = ReplayRunner.MaximumSnapshotWorkUnits,
    long MaximumEventSnapshotWorkUnits = ReplayRunner.MaximumEventSnapshotWorkUnits,
    long MaximumStateMaterializationWorkUnits = ReplayRunner.MaximumStateMaterializationWorkUnits,
    long MaximumTimelineWorkUnits = ReplayRunner.MaximumTimelineWorkUnits)
{
    public static ReplayLimits Default { get; } = new();

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumSnapshotWorkUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEventSnapshotWorkUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumStateMaterializationWorkUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumTimelineWorkUnits);
    }
}
