using IceCrow.Live;
using IceCrow.ProfileSync;
using IceCrow.ProfileSync.Arena;
using IceCrow.ProfileSync.Factories;
using IceCrow.Tracking;
using IceCrow.Tracking.Constructed;

namespace IceCrow.App.Runtime;

/// <summary>
/// Turns finished tracking results into immutable profile events and hands
/// them to the sync queue. It runs on the live consumer thread, so it does
/// only cheap work per update: one record per completed match, one gameplay
/// hold transition, no IO and no HTTP.
/// </summary>
internal sealed class ProfileRecordPipeline
{
    private readonly Func<ProfileEvent, bool> _enqueue;
    private readonly Action<bool> _setGameplayActive;
    private readonly ArenaRunCollector _arenaRuns;
    private (DateTimeOffset StartedAt, DateTimeOffset EndedAt)? _lastBattlegroundsResult;
    private bool _gameplayActive;

    public ProfileRecordPipeline(
        Func<ProfileEvent, bool> enqueue,
        Action<bool> setGameplayActive,
        ArenaRunCollector? arenaRuns = null)
    {
        ArgumentNullException.ThrowIfNull(enqueue);
        ArgumentNullException.ThrowIfNull(setGameplayActive);
        _enqueue = enqueue;
        _setGameplayActive = setGameplayActive;
        _arenaRuns = arenaRuns ?? new ArenaRunCollector();
    }

    public long ConstructedRecords { get; private set; }

    public long ArenaRecords { get; private set; }

    public long BattlegroundsRecords { get; private set; }

    public long IgnoredCompletions { get; private set; }

    /// <param name="update">The routed line.</param>
    /// <param name="gameplayActive">
    /// Whether any tracker currently has an open game; the caller reads it
    /// from the coordinator because an update only carries a snapshot when
    /// the state changed.
    /// </param>
    public void Observe(GameSessionUpdate update, bool gameplayActive)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.CompletedMatch is { } summary)
        {
            RecordCompletedMatch(summary);
        }

        if (update.Battlegrounds is { StateChanged: true, Snapshot: { SessionState: TrackingSessionState.Ended, Result: not null } snapshot })
        {
            RecordBattlegroundsResult(snapshot);
        }

        UpdateGameplayHold(gameplayActive);
    }

    private void RecordCompletedMatch(ConstructedMatchSummary summary)
    {
        switch (summary.Mode)
        {
            case GameMode.Ranked when ConstructedRecordFactory.CreateRanked(summary, Guid.CreateVersion7()) is { } ranked:
                if (_enqueue(ProfileEvent.Create(ProfileEventType.ConstructedMatch, summary.EndedAt, ranked)))
                {
                    ConstructedRecords++;
                }

                break;
            case GameMode.Arena when ConstructedRecordFactory.CreateArena(summary, Guid.CreateVersion7()) is { } arena:
                var association = _arenaRuns.Associate(summary.StartedAt, summary.EndedAt);
                var associated = arena with
                {
                    RunId = association.RunId,
                    ScoreBefore = association.ScoreBefore,
                    ScoreAfter = association.ScoreAfter,
                    ScoreConfidence = association.Confidence,
                };
                if (_enqueue(ProfileEvent.Create(ProfileEventType.ArenaMatch, summary.EndedAt, associated)))
                {
                    ArenaRecords++;
                }

                break;
            default:
                IgnoredCompletions++;
                break;
        }
    }

    private void RecordBattlegroundsResult(TrackingSnapshot snapshot)
    {
        var result = snapshot.Result!;
        var key = (result.StartedAt, result.EndedAt);
        if (_lastBattlegroundsResult == key)
        {
            return;
        }

        _lastBattlegroundsResult = key;
        if (BattlegroundsRecordFactory.Create(snapshot, Guid.CreateVersion7()) is { } record &&
            _enqueue(ProfileEvent.Create(ProfileEventType.BattlegroundsMatch, result.EndedAt, record)))
        {
            BattlegroundsRecords++;
        }
    }

    private void UpdateGameplayHold(bool active)
    {
        if (active == _gameplayActive)
        {
            return;
        }

        _gameplayActive = active;
        _setGameplayActive(active);
    }
}
