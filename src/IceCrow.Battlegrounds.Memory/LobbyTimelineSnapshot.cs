using System.Collections.ObjectModel;

namespace IceCrow.Battlegrounds.Memory;

public sealed class LobbyTimelineSnapshot
{
    private readonly PlayerTimelineSnapshot[] _players;
    private readonly LobbyTimelineEvent[] _events;
    private readonly ReadOnlyCollection<PlayerTimelineSnapshot> _readOnlyPlayers;
    private readonly ReadOnlyCollection<LobbyTimelineEvent> _readOnlyEvents;

    internal LobbyTimelineSnapshot(IEnumerable<PlayerTimeline> players)
    {
        _players = players
            .OrderBy(static player => player.PlayerId)
            .Select(static player => new PlayerTimelineSnapshot(player))
            .ToArray();
        _events = _players
            .SelectMany(static player => player.Events)
            .Order(LobbyTimelineEventComparer.Instance)
            .ToArray();
        _readOnlyPlayers = Array.AsReadOnly(_players);
        _readOnlyEvents = Array.AsReadOnly(_events);
    }

    public static LobbyTimelineSnapshot Empty { get; } = new([]);

    public IReadOnlyList<PlayerTimelineSnapshot> Players => _readOnlyPlayers;

    public IReadOnlyList<LobbyTimelineEvent> Events => _readOnlyEvents;

    public PlayerTimelineSnapshot? GetPlayer(int playerId) =>
        _players.FirstOrDefault(player => player.PlayerId == playerId);
}

public sealed class PlayerTimelineSnapshot
{
    private readonly LobbyTimelineEvent[] _events;
    private readonly ReadOnlyCollection<LobbyTimelineEvent> _readOnlyEvents;

    internal PlayerTimelineSnapshot(PlayerTimeline timeline)
    {
        PlayerId = timeline.PlayerId;
        TavernTier = timeline.TavernTier;
        Triples = timeline.Triples;
        _events = timeline.Events.ToArray();
        _readOnlyEvents = Array.AsReadOnly(_events);
    }

    public int PlayerId { get; }

    public int TavernTier { get; }

    public int Triples { get; }

    public IReadOnlyList<LobbyTimelineEvent> Events => _readOnlyEvents;
}
