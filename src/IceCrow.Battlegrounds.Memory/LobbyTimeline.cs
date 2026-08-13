namespace IceCrow.Battlegrounds.Memory;

public sealed class LobbyTimeline
{
    private readonly Dictionary<int, PlayerTimeline> _players = [];
    private bool _matchActive;
    private BattlegroundsPhase _previousPhase = BattlegroundsPhase.Unknown;

    public IReadOnlyList<PlayerTimeline> Players => _players.Values
        .OrderBy(static player => player.PlayerId)
        .ToArray();

    public IReadOnlyList<LobbyTimelineEvent> Events => _players.Values
        .SelectMany(static player => player.Events)
        .Order(LobbyTimelineEventComparer.Instance)
        .ToArray();

    public PlayerTimeline? GetPlayer(int playerId) => _players.GetValueOrDefault(playerId);

    public bool TryGetPlayer(int playerId, out PlayerTimeline? timeline) =>
        _players.TryGetValue(playerId, out timeline);

    public void Update(
        BattlegroundsState state,
        DateTimeOffset timestamp,
        BoardSnapshot? observedBoard = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.IsActive)
        {
            _matchActive = false;
            _previousPhase = state.Phase;
            return;
        }

        var newMatch = !_matchActive ||
            (state.Phase == BattlegroundsPhase.HeroSelection &&
             _previousPhase != BattlegroundsPhase.HeroSelection);
        if (newMatch)
        {
            _players.Clear();
        }

        _matchActive = true;

        foreach (var player in state.Lobby.Players)
        {
            if (player.PlayerId == state.LocalPlayerId)
            {
                continue;
            }

            GetOrCreate(player.PlayerId).Observe(player, state.Turn, timestamp);
        }

        if (observedBoard is not null)
        {
            GetOrCreate(observedBoard.PlayerId).RecordOpponentObserved(observedBoard);
        }

        _previousPhase = state.Phase;
    }

    public void Reset()
    {
        _players.Clear();
        _matchActive = false;
        _previousPhase = BattlegroundsPhase.Unknown;
    }

    private PlayerTimeline GetOrCreate(int playerId)
    {
        if (_players.TryGetValue(playerId, out var timeline))
        {
            return timeline;
        }

        timeline = new PlayerTimeline(playerId);
        _players.Add(playerId, timeline);
        return timeline;
    }
}
