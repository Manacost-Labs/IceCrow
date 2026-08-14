using IceCrow.Battlegrounds;
using IceCrow.Recording;

namespace IceCrow.FixtureTool;

public static class FixtureExpectedStateBuilder
{
    public const int MaximumGeneratedCheckpoints = 256;

    public static FixtureCheckpointExpectation[] Build(RecordedMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        RecordingSerializer.Validate(match);
        var candidates = SelectCandidateCheckpoints(match);
        var runner = new ReplayRunner(match);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<FixtureCheckpointExpectation>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var state = runner.RunUntilEvent(candidate.EventIndex);
            var baseName = string.IsNullOrWhiteSpace(candidate.Name)
                ? CreateCheckpointName(state)
                : candidate.Name;
            var name = MakeUnique(baseName, names);
            results.Add(new FixtureCheckpointExpectation
            {
                Name = name,
                EventIndex = candidate.EventIndex,
                State = FromReplayState(state),
            });
        }

        return results.ToArray();
    }

    private static List<ReplayCheckpoint> SelectCandidateCheckpoints(RecordedMatch match)
    {
        var byIndex = new SortedDictionary<int, string>();
        foreach (var checkpoint in match.Checkpoints)
        {
            byIndex.TryAdd(checkpoint.EventIndex, checkpoint.Name);
        }

        byIndex.TryAdd(0, "MatchStart");
        for (var index = 0; index < match.Events.Count; index++)
        {
            var recordedEvent = match.Events[index];
            if (recordedEvent.Type == RecordedEventType.MatchEnded)
            {
                byIndex.TryAdd(index, "GameEnd");
                continue;
            }

            if (recordedEvent.Type != RecordedEventType.RawTagChanged)
            {
                continue;
            }

            if (recordedEvent.Tag is "TURN" or "NEXT_OPPONENT_PLAYER_ID" or
                "PLAYER_TECH_LEVEL" or "PLAYER_TRIPLES" or "PLAYSTATE" ||
                recordedEvent.Tag is "2022" or "3533" && recordedEvent.Value == "0")
            {
                byIndex.TryAdd(index, string.Empty);
            }
        }

        byIndex.TryAdd(match.Events.Count - 1, "FinalState");
        if (byIndex.Count > MaximumGeneratedCheckpoints)
        {
            throw new InvalidDataException(
                $"Expected-state template exceeds the {MaximumGeneratedCheckpoints} checkpoint limit. " +
                "Add explicit recording checkpoints or manually reduce the recording first.");
        }

        return byIndex.Select(pair => new ReplayCheckpoint(pair.Value, pair.Key)).ToList();
    }

    internal static FixtureStateExpectation FromReplayState(ReplayState state) =>
        FromState(
            state.Battlegrounds,
            state.OpponentMemory,
            state.Battlegrounds.IsActive ? "Active" :
            state.Battlegrounds.Phase == BattlegroundsPhase.GameOver ? "Ended" : "Inactive");

    internal static FixtureStateExpectation FromTrackingSnapshot(
        Tracking.TrackingSnapshot snapshot) =>
        FromState(
            snapshot.Battlegrounds,
            snapshot.OpponentMemory,
            snapshot.SessionState.ToString());

    private static FixtureStateExpectation FromState(
        BattlegroundsState battlegrounds,
        Battlegrounds.Memory.OpponentMemory memory,
        string sessionState) => new()
        {
            SessionState = sessionState,
            IsActive = battlegrounds.IsActive,
            Turn = battlegrounds.Turn,
            Phase = battlegrounds.Phase.ToString(),
            LocalPlayerId = battlegrounds.LocalPlayerId,
            CurrentOpponentPlayerId = battlegrounds.CurrentOpponentPlayerId,
            LobbyCount = battlegrounds.Lobby.Count,
            OpponentMemory = memory.Histories
                .OrderBy(static pair => pair.Key)
                .Select(static pair => new FixtureOpponentExpectation
                {
                    PlayerId = pair.Key,
                    MinionCount = pair.Value.Latest.Minions.Count,
                    LastSeenTurn = pair.Value.Latest.Turn,
                })
                .ToArray(),
        };

    private static string CreateCheckpointName(ReplayState state) =>
        $"Event{state.CurrentEventIndex}-{state.Battlegrounds.Phase}-Turn{state.Battlegrounds.Turn}";

    private static string MakeUnique(string name, HashSet<string> names)
    {
        var candidate = name;
        var suffix = 2;
        while (!names.Add(candidate))
        {
            candidate = $"{name}-{suffix++}";
        }

        return candidate;
    }
}
