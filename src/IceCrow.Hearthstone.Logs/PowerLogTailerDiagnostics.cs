namespace IceCrow.Hearthstone.Logs;

/// <summary>
/// Bounded reset diagnostics for <see cref="PowerLogTailer"/>. Contains no file
/// paths or log content so it is safe to surface in developer UI or reports.
/// <para>
/// <see cref="FullRereadCount"/> counts resets that discarded a non-zero
/// consumed offset of the same file — i.e. genuine full rereads. The
/// <c>LastReset*</c> fields additionally record checkpoint-discarding path
/// switches (<see cref="LogResetReason.PathChanged"/>), which are not rereads.
/// </para>
/// </summary>
public sealed record PowerLogTailerDiagnostics(
    long FullRereadCount,
    LogResetReason? LastResetReason,
    DateTimeOffset? LastResetAt,
    long LastResetObservedLength,
    long LastResetOffset)
{
    public static PowerLogTailerDiagnostics Empty { get; } = new(0, null, null, 0, 0);
}
