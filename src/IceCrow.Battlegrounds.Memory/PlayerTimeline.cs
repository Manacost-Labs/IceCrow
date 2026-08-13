using System.Collections.ObjectModel;

namespace IceCrow.Battlegrounds.Memory;

public sealed class PlayerTimeline
{
    private readonly List<LobbyTimelineEvent> _events = [];
    private readonly ReadOnlyCollection<LobbyTimelineEvent> _readOnlyEvents;
    private bool _hasBaseline;
    private int _health;
    private int _armor;
    private bool _isAlive;

    internal PlayerTimeline(int playerId)
    {
        PlayerId = playerId;
        _readOnlyEvents = _events.AsReadOnly();
    }

    public int PlayerId { get; }

    public int TavernTier { get; private set; }

    public int Triples { get; private set; }

    public IReadOnlyList<LobbyTimelineEvent> Events => _readOnlyEvents;

    internal void Observe(LobbyPlayer player, int turn, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.PlayerId != PlayerId)
        {
            throw new ArgumentException(
                "The observed player must match this timeline.",
                nameof(player));
        }

        if (!_hasBaseline)
        {
            EstablishBaseline(player);
            return;
        }

        if (player.TavernTier > TavernTier)
        {
            TavernTier = player.TavernTier;
            Add(new TavernUpgraded(PlayerId, turn, timestamp, TavernTier));
        }

        if (player.Triples > Triples)
        {
            var previousTotal = Triples;
            Triples = player.Triples;
            Add(new TripleCreated(
                PlayerId,
                turn,
                timestamp,
                previousTotal,
                Triples));
        }

        ObserveSurvivability(player, turn, timestamp);
    }

    internal void RecordOpponentObserved(BoardSnapshot board)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (board.PlayerId != PlayerId)
        {
            throw new ArgumentException(
                "The observed board must match this timeline.",
                nameof(board));
        }

        if (_events.OfType<OpponentObserved>().Any(observed =>
                observed.Turn == board.Turn &&
                observed.Timestamp == board.Timestamp))
        {
            return;
        }

        Add(new OpponentObserved(
            PlayerId,
            board.Turn,
            board.Timestamp,
            board.Minions.Count));
    }

    private void EstablishBaseline(LobbyPlayer player)
    {
        _hasBaseline = true;
        TavernTier = player.TavernTier;
        Triples = player.Triples;
        _health = player.Health;
        _armor = player.Armor;
        _isAlive = player.IsAlive;
    }

    private void ObserveSurvivability(
        LobbyPlayer player,
        int turn,
        DateTimeOffset timestamp)
    {
        var healthDecreased = player.Health < _health;
        var armorDecreased = player.Armor < _armor;
        if (healthDecreased || armorDecreased)
        {
            int? exactDamage = null;
            if (_armor == 0 && player.Armor == 0 && healthDecreased)
            {
                var healthDifference = (long)_health - player.Health;
                if (healthDifference <= int.MaxValue)
                {
                    exactDamage = (int)healthDifference;
                }
            }

            Add(new DamageTaken(
                PlayerId,
                turn,
                timestamp,
                _health,
                player.Health,
                _armor,
                player.Armor,
                exactDamage));
        }

        if (_isAlive && !player.IsAlive)
        {
            Add(new PlayerDied(
                PlayerId,
                turn,
                timestamp,
                player.Health,
                player.Armor));
        }

        _health = player.Health;
        _armor = player.Armor;
        _isAlive = _isAlive && player.IsAlive;
    }

    private void Add(LobbyTimelineEvent timelineEvent)
    {
        _events.Add(timelineEvent);
        _events.Sort(LobbyTimelineEventComparer.Instance);
    }
}
