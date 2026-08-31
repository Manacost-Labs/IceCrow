using System.Globalization;
using System.Text;
using IceCrow.Battlegrounds;
using IceCrow.Recording;
using IceCrow.Tracking;

namespace IceCrow.FixtureTool;

/// <summary>
/// Privacy-safe analysis of event ordering around each Recruit-to-Combat
/// transition in a recording. Reports only numeric ids, counts, indexes, and
/// time deltas — no entity names, card ids, or raw text. Used to prove when
/// the opponent's board actually becomes visible relative to the phase
/// transition (finding F11).
/// </summary>
public static class CombatWindowAnalyzer
{
    private sealed record BoardFacts(bool IsMinion, bool IsInPlay, int Controller);

    public static async Task<string> AnalyzeAsync(
        string recordingPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingPath);
        var match = await RecordingSerializer
            .LoadAsync(Path.GetFullPath(recordingPath), cancellationToken)
            .ConfigureAwait(false);

        var tracking = new TrackingSession(new TrackingSessionLimits(
            maximumTrackedEntities: ReplayRunner.MaximumReplayEntities,
            trackedEntityWarningThreshold: ReplayRunner.MaximumReplayEntities,
            maximumTagsPerEntity: TrackingSessionLimits.DefaultMaximumTagsPerEntity,
            tagsPerEntityWarningThreshold: TrackingSessionLimits.DefaultMaximumTagsPerEntity,
            maximumLobbyPlayers: ReplayRunner.MaximumLobbyPlayers,
            lobbyPlayerWarningThreshold: ReplayRunner.MaximumLobbyPlayers,
            maximumTimelineEventsPerPlayer: TrackingSessionLimits.DefaultMaximumTimelineEventsPerPlayer,
            timelineEventWarningThreshold: TrackingSessionLimits.DefaultMaximumTimelineEventsPerPlayer,
            maximumOpponentSnapshotsPerPlayer: ReplayRunner.MaximumOpponentSnapshots,
            opponentSnapshotWarningThreshold: ReplayRunner.MaximumOpponentSnapshots));

        var facts = new Dictionary<int, BoardFacts>();
        var summary = new StringBuilder();
        summary.AppendLine("COMBAT WINDOW ANALYSIS (counts, ids, and deltas only)");

        var combatNumber = 0;
        var armedOpponent = (int?)null;
        var armedIndex = 0;
        var armedTimestamp = DateTimeOffset.MinValue;
        var firstNonEmptyReported = true;
        var peakEligible = 0;
        var peakByController = new SortedDictionary<int, int>();

        for (var index = 0; index < match.Events.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recorded = match.Events[index];
            TrackingUpdate update;
            switch (recorded.Type)
            {
                case RecordedEventType.MatchStarted:
                    update = tracking.StartBattlegroundsMatch(
                        recorded.Timestamp,
                        recorded.PlayerId);
                    break;
                case RecordedEventType.MatchEnded:
                    update = tracking.EndMatch(recorded.Timestamp);
                    break;
                default:
                    update = tracking.Apply(recorded.ToGameEvent());
                    break;
            }

            if (update.Entity is { } entity)
            {
                facts[entity.Id] = new BoardFacts(
                    entity.IsMinion,
                    entity.IsInPlay,
                    entity.Controller);
            }

            var phase = update.Battlegrounds.Phase;
            if (armedOpponent is int opponent)
            {
                var eligible = CountEligible(facts, opponent);
                peakEligible = Math.Max(peakEligible, eligible);
                foreach (var entry in CurrentByController(facts))
                {
                    peakByController[entry.Key] = Math.Max(
                        peakByController.GetValueOrDefault(entry.Key),
                        entry.Value);
                }
                if (!firstNonEmptyReported && eligible > 0)
                {
                    firstNonEmptyReported = true;
                    summary.AppendLine(CultureInfo.InvariantCulture,
                        $"  first non-empty board : +{index - armedIndex} events, " +
                        $"+{(recorded.Timestamp - armedTimestamp).TotalMilliseconds:F0} ms, " +
                        $"{eligible} eligible minions");
                }

                if (phase != BattlegroundsPhase.Combat)
                {
                    var neverNote = firstNonEmptyReported
                        ? string.Empty
                        : ", board never non-empty";
                    summary.AppendLine(string.Create(CultureInfo.InvariantCulture,
                        $"  combat window ends    : +{index - armedIndex} events, peak eligible {peakEligible}{neverNote}"));
                    summary.AppendLine(
                        "  peak in-play minions by controller: " +
                        (peakByController.Count == 0
                            ? "none"
                            : string.Join(", ", peakByController.Select(pair =>
                                string.Create(CultureInfo.InvariantCulture,
                                    $"controller {pair.Key}: {pair.Value}")))));
                    peakByController.Clear();
                    armedOpponent = null;
                }
            }

            if (update.PreviousPhase != BattlegroundsPhase.Combat &&
                phase == BattlegroundsPhase.Combat)
            {
                combatNumber++;
                armedOpponent = update.Battlegrounds.CurrentOpponentPlayerId;
                armedIndex = index;
                armedTimestamp = recorded.Timestamp;
                peakEligible = armedOpponent is int armed
                    ? CountEligible(facts, armed)
                    : 0;
                firstNonEmptyReported = peakEligible > 0;
                summary.AppendLine(CultureInfo.InvariantCulture,
                    $"Combat #{combatNumber} · turn {update.Battlegrounds.Turn} · " +
                    $"opponent {armedOpponent?.ToString(CultureInfo.InvariantCulture) ?? "-"} · " +
                    $"event {index}");
                var capturedNote = update.ObservedBoard is { } observed
                    ? string.Create(CultureInfo.InvariantCulture,
                        $" · captured board minions: {observed.Minions.Count}")
                    : " · no board captured";
                summary.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  eligible at transition: {peakEligible}{capturedNote}"));
            }
        }

        return summary.ToString();
    }

    private static SortedDictionary<int, int> CurrentByController(
        Dictionary<int, BoardFacts> facts)
    {
        var counts = new SortedDictionary<int, int>();
        foreach (var entry in facts.Values)
        {
            if (entry.IsMinion && entry.IsInPlay)
            {
                counts[entry.Controller] = counts.GetValueOrDefault(entry.Controller) + 1;
            }
        }

        return counts;
    }

    private static int CountEligible(Dictionary<int, BoardFacts> facts, int opponentId)
    {
        var count = 0;
        foreach (var entry in facts.Values)
        {
            if (entry.IsMinion && entry.IsInPlay && entry.Controller == opponentId)
            {
                count++;
            }
        }

        return count;
    }
}
