using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;

namespace IceCrow.Overlay;

internal sealed record OpponentLobbyTileViewState(
    int PlayerId,
    string HeroDisplay,
    string? HeroCardId,
    string TavernTierLine,
    string StatusLine,
    string LastSeenLine,
    string AgeLine,
    IReadOnlyList<string> ProgressionRows,
    string TriplesLine,
    IReadOnlyList<string> BoardRows)
{
    public static IReadOnlyList<OpponentLobbyTileViewState> Create(
        BattlegroundsState state,
        OpponentMemory memory,
        LobbyTimeline? timeline = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(memory);

        return state.Lobby.Players
            .Where(player => player.PlayerId != state.LocalPlayerId)
            .Select(player => Create(
                player,
                memory.GetLatest(player.PlayerId),
                timeline?.GetPlayer(player.PlayerId),
                state.Turn))
            .ToArray();
    }

    private static OpponentLobbyTileViewState Create(
        LobbyPlayer player,
        BoardSnapshot? board,
        PlayerTimeline? timeline,
        int currentTurn)
    {
        var heroDisplay = FirstAvailable(
            player.HeroName,
            player.HeroCardId,
            $"Player {player.PlayerId}");
        var boardRows = CreateBoardRows(board);
        var status = board switch
        {
            null => "NOT FOUGHT YET",
            { Minions.Count: 0 } => "EMPTY BOARD",
            _ => $"Last Seen: Turn {board.Turn}",
        };

        return new OpponentLobbyTileViewState(
            player.PlayerId,
            heroDisplay,
            player.HeroCardId,
            $"Tavern Tier: {player.TavernTier}",
            status,
            board is null ? string.Empty : $"Last Seen: Turn {board.Turn}",
            board is null ? string.Empty : $"Age: {board.GetAge(currentTurn)} turns",
            CreateProgressionRows(timeline),
            $"Triples: {timeline?.Triples ?? player.Triples}",
            boardRows);
    }

    private static string[] CreateProgressionRows(PlayerTimeline? timeline) =>
        timeline?.Events
            .OfType<TavernUpgraded>()
            .Select(upgrade => $"T{upgrade.TavernTier} → Turn {upgrade.Turn}")
            .ToArray() ?? [];

    private static string[] CreateBoardRows(BoardSnapshot? board)
    {
        if (board is null)
        {
            return ["NOT FOUGHT YET"];
        }

        if (board.Minions.Count == 0)
        {
            return ["EMPTY BOARD"];
        }

        return board.Minions
            .Select(minion =>
                $"{minion.ZonePosition}. {FirstAvailable(minion.CardId, $"Entity {minion.EntityId}")}  {minion.Attack}/{minion.Health}")
            .ToArray();
    }

    private static string FirstAvailable(params string?[] values) =>
        values.First(static value => !string.IsNullOrWhiteSpace(value))!;
}
