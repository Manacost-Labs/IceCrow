using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IceCrow.Recording;

namespace IceCrow.FixtureTool;

public sealed record RecordingAnalysisOptions(
    string? FocusTag = null,
    int TopTags = 20,
    int? AroundEvent = null,
    int AroundWindow = 20);

/// <summary>
/// Privacy-safe analysis of a recorded match. Emits only aggregates: event
/// type counts, tag frequencies, TURN and focus-tag occurrence lists, and
/// entity-reference statistics. Entity names, BattleTags, account ids, and
/// free-text content are never printed; tag values are shown only when they
/// match a safe token shape.
/// </summary>
public static partial class RecordingAnalyzer
{
    [GeneratedRegex("^[A-Za-z0-9_.-]{1,32}$")]
    private static partial Regex SafeTokenPattern();

    public static async Task<string> AnalyzeAsync(
        string recordingPath,
        RecordingAnalysisOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingPath);
        ArgumentNullException.ThrowIfNull(options);
        var match = await RecordingSerializer
            .LoadAsync(Path.GetFullPath(recordingPath), cancellationToken)
            .ConfigureAwait(false);
        return Analyze(match, options);
    }

    public static string Analyze(RecordedMatch match, RecordingAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(options);

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"Events: {match.Events.Count}");
        AppendTypeCounts(report, match);
        AppendEntityReferenceStats(report, match);
        AppendTagFrequency(report, match, options.TopTags);
        AppendTagOccurrences(report, match, "TURN");
        if (options.FocusTag is { } focusTag &&
            !string.Equals(focusTag, "TURN", StringComparison.Ordinal))
        {
            AppendTagOccurrences(report, match, focusTag);
        }

        if (options.AroundEvent is { } center)
        {
            AppendWindow(report, match, center, options.AroundWindow);
        }

        return report.ToString();
    }

    private static void AppendTypeCounts(StringBuilder report, RecordedMatch match)
    {
        report.AppendLine("Event types:");
        foreach (var group in match.Events
                     .GroupBy(static recordedEvent => recordedEvent.Type)
                     .OrderByDescending(static group => group.Count()))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  {group.Key,-22} {group.Count()}");
        }
    }

    private static void AppendEntityReferenceStats(StringBuilder report, RecordedMatch match)
    {
        var withId = 0;
        var namedOnly = 0;
        foreach (var recordedEvent in match.Events)
        {
            if (recordedEvent.Type != RecordedEventType.RawTagChanged)
            {
                continue;
            }

            if (recordedEvent.EntityId is not null)
            {
                withId++;
            }
            else if (recordedEvent.EntityName is not null)
            {
                namedOnly++;
            }
        }

        report.AppendLine(CultureInfo.InvariantCulture,
            $"RawTagChanged entity references: numeric-id={withId} named-only={namedOnly}");
    }

    private static void AppendTagFrequency(StringBuilder report, RecordedMatch match, int topTags)
    {
        report.AppendLine(CultureInfo.InvariantCulture, $"Top {topTags} tags:");
        var frequencies = match.Events
            .Where(static recordedEvent =>
                recordedEvent is { Type: RecordedEventType.RawTagChanged, Tag: not null })
            .GroupBy(static recordedEvent => recordedEvent.Tag!, StringComparer.Ordinal)
            .OrderByDescending(static group => group.Count())
            .Take(topTags);
        foreach (var group in frequencies)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {SafeToken(group.Key),-28} {group.Count()}");
        }
    }

    private static void AppendTagOccurrences(
        StringBuilder report,
        RecordedMatch match,
        string tag)
    {
        report.AppendLine(CultureInfo.InvariantCulture, $"Tag '{SafeToken(tag)}' occurrences:");
        var occurrences = 0;
        for (var index = 0; index < match.Events.Count; index++)
        {
            var recordedEvent = match.Events[index];
            if (recordedEvent.Type != RecordedEventType.RawTagChanged ||
                !string.Equals(recordedEvent.Tag, tag, StringComparison.Ordinal))
            {
                continue;
            }

            occurrences++;
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  [{index,6}] {recordedEvent.Timestamp:HH:mm:ss.fff} " +
                $"entity={DescribeEntityReference(recordedEvent)} " +
                $"value={SafeToken(recordedEvent.Value)}");
        }

        if (occurrences == 0)
        {
            report.AppendLine("  none");
        }
    }

    private static void AppendWindow(
        StringBuilder report,
        RecordedMatch match,
        int center,
        int window)
    {
        var first = Math.Max(0, center - window);
        var last = Math.Min(match.Events.Count - 1, center + window);
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Events {first}..{last} (around {center}):");
        for (var index = first; index <= last; index++)
        {
            var recordedEvent = match.Events[index];
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  [{index,6}] {recordedEvent.Type} " +
                $"entity={DescribeEntityReference(recordedEvent)} " +
                $"tag={SafeToken(recordedEvent.Tag)} value={SafeToken(recordedEvent.Value)}");
        }
    }

    private static string DescribeEntityReference(RecordedEvent recordedEvent)
    {
        if (recordedEvent.EntityId is int entityId)
        {
            return entityId.ToString(CultureInfo.InvariantCulture);
        }

        // Never print the name itself; only classify the reference shape.
        return recordedEvent.EntityName is null ? "-" : "named-only";
    }

    private static string SafeToken(string? value)
    {
        if (value is null)
        {
            return "-";
        }

        return SafeTokenPattern().IsMatch(value) ? value : "<redacted>";
    }
}
