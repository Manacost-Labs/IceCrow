using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Data;
using IceCrow.Tracking;

namespace IceCrow.Presentation;

public static class BattlegroundsOverlayViewStateFactory
{
    public static BattlegroundsOverlayViewState Create(
        TrackingSnapshot snapshot,
        ICardDatabase? cardDatabase = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var state = snapshot.Battlegrounds;
        var opponents = state.Lobby.Players
            .Where(player => player.PlayerId != state.LocalPlayerId)
            .Select(player => CreateOpponent(
                player,
                snapshot.OpponentMemory.GetLatest(player.PlayerId),
                snapshot.LobbyTimeline.GetPlayer(player.PlayerId),
                state.Turn,
                cardDatabase));

        return new BattlegroundsOverlayViewState(
            state.IsActive && state.Lobby.Count > 1,
            opponents);
    }

    private static OpponentOverlayViewState CreateOpponent(
        LobbyPlayer player,
        BoardSnapshot? board,
        PlayerTimelineSnapshot? timeline,
        int currentTurn,
        ICardDatabase? cardDatabase)
    {
        var heroDisplay = FirstAvailable(
            player.HeroName,
            player.HeroCardId,
            $"Player {player.PlayerId}");
        var status = board switch
        {
            null => "NOT FOUGHT YET",
            { Minions.Count: 0 } => "EMPTY BOARD",
            _ => $"Last Seen: Turn {board.Turn}",
        };

        return new OpponentOverlayViewState(
            player.PlayerId,
            heroDisplay,
            player.HeroCardId,
            $"Tavern Tier: {player.TavernTier}",
            status,
            board is null ? string.Empty : $"Last Seen: Turn {board.Turn}",
            board is null ? string.Empty : $"Age: {board.GetAge(currentTurn)} turns",
            CreateProgressionRows(timeline),
            $"Triples: {timeline?.Triples ?? player.Triples}",
            CreateBoardRows(board, cardDatabase));
    }

    private static IEnumerable<string> CreateProgressionRows(PlayerTimelineSnapshot? timeline) =>
        timeline?.Events
            .OfType<TavernUpgraded>()
            .Select(upgrade => $"T{upgrade.TavernTier} → Turn {upgrade.Turn}") ?? [];

    private static IEnumerable<string> CreateBoardRows(BoardSnapshot? board, ICardDatabase? cardDatabase)
    {
        if (board is null)
        {
            return ["NOT FOUGHT YET"];
        }

        if (board.Minions.Count == 0)
        {
            return ["EMPTY BOARD"];
        }

        return board.Minions.Select(minion =>
        {
            var definition = string.IsNullOrWhiteSpace(minion.CardId)
                ? null
                : cardDatabase?.GetByCardId(minion.CardId);
            var display = FirstAvailable(definition?.Name, minion.CardId, $"Entity {minion.EntityId}");
            var tier = definition?.TavernTier is int tavernTier ? $" [T{tavernTier}]" : string.Empty;
            return $"{minion.ZonePosition}. {display}{tier}  {minion.Attack}/{minion.Health}";
        });
    }

    private static string FirstAvailable(params string?[] values) =>
        values.First(static value => !string.IsNullOrWhiteSpace(value))!;
}
