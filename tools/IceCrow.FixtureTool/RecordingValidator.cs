using System.Globalization;
using System.Text;
using IceCrow.Recording;

namespace IceCrow.FixtureTool;

/// <summary>
/// Official validation of one private recording: load through the official
/// serializer, replay through the official runner, and print a privacy-safe
/// semantic summary. No entity names, BattleTags, account ids, or raw text.
/// </summary>
public static class RecordingValidator
{
    // Measurement-only budgets for AnalyzeReplayWorkAsync: far above any
    // writer-acceptable recording (honest event-snapshot work is bounded by
    // MaximumEventCount x (1 + tags-per-entity cap) ~= 64M) so the run always
    // completes and reports what the default limits would have been charged.
    private static readonly ReplayLimits MeasurementLimits = new(
        MaximumSnapshotWorkUnits: 1_000_000_000,
        MaximumEventSnapshotWorkUnits: 1_000_000_000,
        MaximumStateMaterializationWorkUnits: 1_000_000_000,
        MaximumTimelineWorkUnits: 1_000_000_000);

    public static async Task<string> ValidateAsync(
        string recordingPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingPath);
        var match = await RecordingSerializer
            .LoadAsync(Path.GetFullPath(recordingPath), cancellationToken)
            .ConfigureAwait(false);
        var runner = new ReplayRunner(match);
        var state = runner.RunAll(cancellationToken);
        var battlegrounds = state.Battlegrounds;

        var summary = new StringBuilder();
        summary.AppendLine("OFFICIAL VALIDATION PASSED");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"format version       : {match.FormatVersion}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"capture started      : {match.StartedAt:yyyy-MM-dd HH:mm:ss.fff zzz}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"capture ended        : {match.Events[^1].Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"events               : {match.Events.Count}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"replayed             : {state.ProcessedEventCount}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"turn                 : {battlegrounds.Turn}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"phase                : {battlegrounds.Phase}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"lobby count          : {battlegrounds.Lobby.Count}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"opponent histories   : {state.OpponentMemory.Histories.Count}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"timeline events      : {state.LobbyTimeline.Events.Count}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"unresolved named refs: {state.UnresolvedNamedReferences}");
        AppendWork(summary, runner.WorkDiagnostics);
        return summary.ToString();
    }

    /// <summary>
    /// Loads a recording and replays it under measurement-only budgets to
    /// report the work the default limits would be charged. Used to diagnose
    /// and calibrate replay work guards without weakening them.
    /// </summary>
    public static async Task<string> AnalyzeReplayWorkAsync(
        string recordingPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingPath);
        var match = await RecordingSerializer
            .LoadAsync(Path.GetFullPath(recordingPath), cancellationToken)
            .ConfigureAwait(false);
        var runner = new ReplayRunner(match, MeasurementLimits);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _ = runner.RunAll(cancellationToken);
        stopwatch.Stop();

        var work = runner.WorkDiagnostics;
        var summary = new StringBuilder();
        summary.AppendLine("REPLAY WORK MEASUREMENT (measurement-only limits)");
        AppendWork(summary, work);
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"work per event       : {(double)work.EventSnapshotWorkUnits / Math.Max(1, work.ProcessedEventCount):F2}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"replay elapsed       : {stopwatch.Elapsed.TotalSeconds:F2} s");
        return summary.ToString();
    }

    private static void AppendWork(StringBuilder summary, ReplayWorkDiagnostics work)
    {
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"events processed     : {work.ProcessedEventCount}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"event snapshot work  : {work.EventSnapshotWorkUnits}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"timeline work        : {work.TimelineWorkUnits}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"board snapshot work  : {work.BoardSnapshotWorkUnits}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"state materialization: {work.StateMaterializationWorkUnits}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"entities             : {work.EntityCount}");
        summary.AppendLine(CultureInfo.InvariantCulture,
            $"max tags/entity      : {work.MaximumTagsOnEntity}");
    }
}
