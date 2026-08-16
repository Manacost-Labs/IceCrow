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
                snapshot.OpponentMemory.GetHistory(player.PlayerId),
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
        OpponentBoardHistory? history,
        PlayerTimelineSnapshot? timeline,
        int currentTurn,
        ICardDatabase? cardDatabase,
        ICardArtSource? cardArtSource)
    {
        var board = history?.Latest;
        var heroName = ResolveHeroName(player, cardDatabase);
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
            CreateBoard(board, cardDatabase, cardArtSource),
            CreateChanges(history, cardDatabase));
    }

    /// <summary>
    /// Hero names come from card metadata first (which also resolves skin card
    /// ids), then the entity name the log provided. A raw CardId is never shown
    /// as a name: without metadata the row reads "Unknown hero" and the raw id
    /// stays available on <see cref="OpponentOverlayViewState.HeroCardId"/> for
    /// tooltips and diagnostics.
    /// </summary>
    private static string ResolveHeroName(LobbyPlayer player, ICardDatabase? cardDatabase)
    {
        var definition = string.IsNullOrWhiteSpace(player.HeroCardId)
            ? null
            : cardDatabase?.GetHeroByCardId(player.HeroCardId);
        if (definition is not null)
        {
            return definition.Name;
        }

        if (!string.IsNullOrWhiteSpace(player.HeroName))
        {
            return player.HeroName;
        }

        return string.IsNullOrWhiteSpace(player.HeroCardId)
            ? string.Create(CultureInfo.InvariantCulture, $"Player {player.PlayerId}")
            : "Unknown hero";
    }

    private static OpponentChangesViewState? CreateChanges(
        OpponentBoardHistory? history,
        ICardDatabase? cardDatabase)
    {
        if (history?.Previous is not BoardSnapshot previous)
        {
            return null;
        }

        var changes = OpponentBoardDiffCalculator.Compare(previous, history.Latest);
        var rows = new List<MinionChangeViewState>(
            changes.AddedMinions.Count + changes.ChangedMinions.Count + changes.RemovedMinions.Count);
        foreach (var added in changes.AddedMinions)
        {
            rows.Add(MinionChangeViewState.Added(
                ResolveMinionName(added, cardDatabase),
                added.Attack,
                added.Health));
        }

        foreach (var change in changes.ChangedMinions)
        {
            // Position-only movement stays out of the v1 list on purpose.
            if (change.HasStatChange)
            {
                rows.Add(MinionChangeViewState.Changed(
                    ResolveMinionName(change.Current, cardDatabase),
                    change.Previous.Attack,
                    change.Previous.Health,
                    change.Current.Attack,
                    change.Current.Health,
                    ToConfidence(change.Identity)));
            }
        }

        foreach (var removed in changes.RemovedMinions)
        {
            rows.Add(MinionChangeViewState.Removed(ResolveMinionName(removed, cardDatabase)));
        }

        return new OpponentChangesViewState(
            changes.PreviousTurn,
            changes.CurrentTurn,
            changes.Significance == BoardChangeSignificance.Major,
            rows);
    }

    /// <summary>Confidence may be renamed for the UI but never increased.</summary>
    private static MinionChangeConfidence ToConfidence(MinionIdentity identity) => identity switch
    {
        MinionIdentity.SameEntity => MinionChangeConfidence.Exact,
        MinionIdentity.LikelySameCard => MinionChangeConfidence.Likely,
        _ => MinionChangeConfidence.Ambiguous,
    };

    private static string ResolveMinionName(MinionSnapshot minion, ICardDatabase? cardDatabase)
    {
        var definition = string.IsNullOrWhiteSpace(minion.CardId)
            ? null
            : cardDatabase?.GetByCardId(minion.CardId);
        return FirstAvailable(
            definition?.Name,
            minion.CardId,
            string.Create(CultureInfo.InvariantCulture, $"Entity {minion.EntityId}"));
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

            return MinionTileViewState.Create(
                minion.ZonePosition,
                minion.CardId,
                ResolveMinionName(minion, cardDatabase),
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
