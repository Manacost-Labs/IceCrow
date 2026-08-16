namespace IceCrow.Battlegrounds.Memory;

public sealed class LobbyTimeline
{
    private readonly Dictionary<int, PlayerTimeline> _players = [];
    private readonly int _maximumPlayers;
    private readonly int _maximumEventsPerPlayer;
    private bool _matchActive;
    private BattlegroundsPhase _previousPhase = BattlegroundsPhase.Unknown;

    public LobbyTimeline(
        int maximumPlayers = 16,
        int maximumEventsPerPlayer = 512)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPlayers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEventsPerPlayer);
        _maximumPlayers = maximumPlayers;
        _maximumEventsPerPlayer = maximumEventsPerPlayer;
    }

    public int EventCount => _players.Values.Sum(static player => player.Events.Count);

    /// <summary>
    /// Sum of actual timeline mutations (inserts and evictions) across the
    /// lobby. Monotonic within one match; a new match clears it with the
    /// per-player timelines.
    /// </summary>
    public long MutationWorkUnits =>
        _players.Values.Sum(static player => player.MutationWorkUnits);

    public int MaximumPlayerEventCount => _players.Count == 0
        ? 0
        : _players.Values.Max(static player => player.Events.Count);

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

    public LobbyTimelineSnapshot CreateSnapshot() => new(_players.Values);

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

            GetOrCreate(player.PlayerId)?.Observe(player, state.Turn, timestamp);
        }

        if (observedBoard is not null)
        {
            GetOrCreate(observedBoard.PlayerId)?.RecordOpponentObserved(observedBoard);
        }

        _previousPhase = state.Phase;
    }

    public void Reset()
    {
        _players.Clear();
        _matchActive = false;
        _previousPhase = BattlegroundsPhase.Unknown;
    }

    private PlayerTimeline? GetOrCreate(int playerId)
    {
        if (_players.TryGetValue(playerId, out var timeline))
        {
            return timeline;
        }

        if (_players.Count >= _maximumPlayers)
        {
            return null;
        }

        timeline = new PlayerTimeline(playerId, _maximumEventsPerPlayer);
        _players.Add(playerId, timeline);
        return timeline;
    }
}
