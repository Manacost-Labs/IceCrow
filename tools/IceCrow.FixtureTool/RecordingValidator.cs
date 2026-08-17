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
    public static async Task<string> ValidateAsync(
        string recordingPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingPath);
        var match = await RecordingSerializer
            .LoadAsync(Path.GetFullPath(recordingPath), cancellationToken)
            .ConfigureAwait(false);
        var state = new ReplayRunner(match).RunAll(cancellationToken);
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
        return summary.ToString();
    }
}
