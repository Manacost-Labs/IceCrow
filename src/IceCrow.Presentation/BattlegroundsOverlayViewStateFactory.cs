using System.Globalization;
using IceCrow.Battlegrounds;
using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Data;
using IceCrow.Tracking;

namespace IceCrow.Presentation;

public static class BattlegroundsOverlayViewStateFactory
{
    public static BattlegroundsOverlayViewState Create(
        TrackingSnapshot snapshot,
        ICardDatabase? cardDatabase = null,
        ICardArtSource? cardArtSource = null)
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
                cardDatabase,
                cardArtSource));

        return new BattlegroundsOverlayViewState(
            state.IsActive && state.Lobby.Count > 1,
            opponents);
    }

    private static OpponentOverlayViewState CreateOpponent(
        LobbyPlayer player,
        BoardSnapshot? board,
        PlayerTimelineSnapshot? timeline,
        int currentTurn,
        ICardDatabase? cardDatabase,
        ICardArtSource? cardArtSource)
    {
        var heroName = FirstAvailable(
            player.HeroName,
            player.HeroCardId,
            string.Create(CultureInfo.InvariantCulture, $"Player {player.PlayerId}"));
        var presence = board switch
        {
            null => OpponentPresence.NotFought,
            { Minions.Count: 0 } => OpponentPresence.EmptyBoard,
            _ => OpponentPresence.Seen,
        };

        return new OpponentOverlayViewState(
            player.PlayerId,
            heroName,
            player.HeroCardId,
            player.TavernTier,
            player.Health,
            player.Armor,
            presence,
            board?.Turn,
            board?.GetAge(currentTurn),
            timeline?.Triples ?? player.Triples,
            CreateProgressionRows(timeline),
            CreateBoard(board, cardDatabase, cardArtSource));
    }

    private static IEnumerable<string> CreateProgressionRows(PlayerTimelineSnapshot? timeline) =>
        timeline?.Events
            .OfType<TavernUpgraded>()
            .Select(upgrade => string.Create(
                CultureInfo.InvariantCulture,
                $"T{upgrade.TavernTier}·{upgrade.Turn}")) ?? [];

    private static IEnumerable<MinionTileViewState> CreateBoard(
        BoardSnapshot? board,
        ICardDatabase? cardDatabase,
        ICardArtSource? cardArtSource)
    {
        if (board is null || board.Minions.Count == 0)
        {
            return [];
        }

        return board.Minions.Select(minion =>
        {
            var definition = string.IsNullOrWhiteSpace(minion.CardId)
                ? null
                : cardDatabase?.GetByCardId(minion.CardId);
            var displayName = FirstAvailable(
                definition?.Name,
                minion.CardId,
                string.Create(CultureInfo.InvariantCulture, $"Entity {minion.EntityId}"));

            return MinionTileViewState.Create(
                minion.ZonePosition,
                minion.CardId,
                displayName,
                minion.Attack,
                minion.Health,
                definition?.TavernTier,
                TryGetArtPath(minion.CardId, cardArtSource));
        });
    }

    private static string? TryGetArtPath(string? cardId, ICardArtSource? cardArtSource)
    {
        if (cardArtSource is null || string.IsNullOrWhiteSpace(cardId))
        {
            return null;
        }

        return cardArtSource.TryGetArtPath(cardId, out var artPath) ? artPath : null;
    }

    private static string FirstAvailable(params string?[] values) =>
        values.First(static value => !string.IsNullOrWhiteSpace(value))!;
}
