using System.Collections.ObjectModel;

namespace IceCrow.Battlegrounds;

public sealed class LobbyState : IEquatable<LobbyState>
{
    private static readonly LobbyPlayer[] NoPlayers = [];
    private readonly LobbyPlayer[] _players;
    private readonly ReadOnlyCollection<LobbyPlayer> _readOnlyPlayers;

    private LobbyState(LobbyPlayer[] players)
    {
        _players = players;
        _readOnlyPlayers = Array.AsReadOnly(_players);
    }

    public static LobbyState Empty { get; } = new(NoPlayers);

    public IReadOnlyList<LobbyPlayer> Players => _readOnlyPlayers;

    public int Count => _players.Length;

    public LobbyPlayer? GetPlayer(int playerId)
    {
        var index = FindPlayerIndex(playerId);
        return index >= 0 ? _players[index] : null;
    }

    public bool TryGetPlayer(int playerId, out LobbyPlayer? player)
    {
        player = GetPlayer(playerId);
        return player is not null;
    }

    public LobbyState SetPlayer(LobbyPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var index = FindPlayerIndex(player.PlayerId);
        if (index >= 0 && _players[index] == player)
        {
            return this;
        }

        if (index >= 0)
        {
            var updated = (LobbyPlayer[])_players.Clone();
            updated[index] = player;
            return new LobbyState(updated);
        }

        var insertionIndex = ~index;
        var expanded = new LobbyPlayer[_players.Length + 1];
        Array.Copy(_players, 0, expanded, 0, insertionIndex);
        expanded[insertionIndex] = player;
        Array.Copy(
            _players,
            insertionIndex,
            expanded,
            insertionIndex + 1,
            _players.Length - insertionIndex);
        return new LobbyState(expanded);
    }

    public bool Equals(LobbyState? other) =>
        ReferenceEquals(this, other) ||
        (other is not null && _players.AsSpan().SequenceEqual(other._players));

    public override bool Equals(object? obj) => obj is LobbyState other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var player in _players)
        {
            hash.Add(player);
        }

        return hash.ToHashCode();
    }

    private int FindPlayerIndex(int playerId) =>
        FindPlayerIndexCore(playerId);

    private int FindPlayerIndexCore(int playerId)
    {
        var lower = 0;
        var upper = _players.Length - 1;
        while (lower <= upper)
        {
            var middle = lower + ((upper - lower) / 2);
            var comparison = _players[middle].PlayerId.CompareTo(playerId);
            if (comparison == 0)
            {
                return middle;
            }

            if (comparison < 0)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle - 1;
            }
        }

        return ~lower;
    }
}
