using IceCrow.Hearthstone.Entities;

namespace IceCrow.Battlegrounds;

public static class BattlegroundsReducer
{
    public static BattlegroundsState Apply(
        BattlegroundsState state,
        BattlegroundsEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(gameEvent);

        return gameEvent switch
        {
            BattlegroundsGameStarted started => StartGame(started),
            BattlegroundsEntityChanged changed => ApplyEntityChange(state, changed),
            BattlegroundsEntityObserved observed => ObserveEntity(state, observed.Entity),
            BattlegroundsGameEnded when state.IsActive => EndGame(state),
            _ => state,
        };
    }

    private static BattlegroundsState StartGame(BattlegroundsGameStarted started) => new(
        IsActive: true,
        Turn: 0,
        Phase: BattlegroundsPhase.HeroSelection,
        LocalPlayerId: PositiveOrNull(started.LocalPlayerId),
        CurrentOpponentPlayerId: null,
        Lobby: LobbyState.Empty);

    private static BattlegroundsState ApplyEntityChange(
        BattlegroundsState state,
        BattlegroundsEntityChanged changed)
    {
        if (!state.IsActive)
        {
            return state;
        }

        state = ObserveEntity(state, changed.Entity);
        var mutation = changed.Mutation;

        if (mutation.Tag == GameTag.Turn)
        {
            var turn = CalculateTurn(mutation.Value);
            return state with
            {
                Turn = turn,
                Phase = turn > 0 ? BattlegroundsPhase.Recruit : state.Phase,
            };
        }

        if (IsCombatTransition(mutation))
        {
            return state with { Phase = BattlegroundsPhase.Combat };
        }

        if (mutation.Tag == GameTag.PlayState &&
            changed.Entity.PlayerId == state.LocalPlayerId &&
            IsTerminalPlayState(mutation.Value))
        {
            return EndGame(state);
        }

        return state;
    }

    private static BattlegroundsState ObserveEntity(
        BattlegroundsState state,
        EntitySnapshot entity)
    {
        if (!state.IsActive)
        {
            return state;
        }

        var playerId = PositiveOrNull(entity.PlayerId);
        var localPlayerId = state.LocalPlayerId;
        if (playerId is int observedPlayerId && entity.GetTag(GameTag.CurrentPlayer) > 0)
        {
            localPlayerId = observedPlayerId;
        }

        var lobby = state.Lobby;
        if (playerId is int lobbyPlayerId)
        {
            var existing = lobby.GetPlayer(lobbyPlayerId) ?? LobbyPlayer.Create(lobbyPlayerId);
            var heroEntityId = PositiveOrNull(entity.GetTag(GameTag.HeroEntity)) ??
                               existing.HeroEntityId;
            var isHero = entity.CardType == CardType.Hero || existing.HeroEntityId == entity.Id;
            if (isHero)
            {
                heroEntityId = entity.Id;
            }

            var updated = existing with
            {
                HeroEntityId = heroEntityId,
                HeroCardId = isHero && !string.IsNullOrWhiteSpace(entity.CardId)
                    ? entity.CardId
                    : existing.HeroCardId,
                Health = HasAnyTag(entity, GameTag.Health, GameTag.Damage)
                    ? entity.Health
                    : existing.Health,
                Armor = HasTag(entity, GameTag.Armor)
                    ? entity.GetTag(GameTag.Armor)
                    : existing.Armor,
                TavernTier = HasTag(entity, GameTag.PlayerTechLevel)
                    ? entity.GetTag(GameTag.PlayerTechLevel)
                    : existing.TavernTier,
                Triples = HasTag(entity, GameTag.PlayerTriples)
                    ? entity.GetTag(GameTag.PlayerTriples)
                    : existing.Triples,
                IsAlive = HasTag(entity, GameTag.PlayState)
                    ? IsAlivePlayState(entity.GetTag(GameTag.PlayState))
                    : existing.IsAlive,
            };
            lobby = lobby.SetPlayer(updated);
        }

        var currentOpponentPlayerId = state.CurrentOpponentPlayerId;
        if (playerId == localPlayerId && HasTag(entity, GameTag.NextOpponentPlayerId))
        {
            currentOpponentPlayerId = PositiveOrNull(
                entity.GetTag(GameTag.NextOpponentPlayerId));
        }

        return state with
        {
            LocalPlayerId = localPlayerId,
            CurrentOpponentPlayerId = currentOpponentPlayerId,
            Lobby = lobby,
        };
    }

    private static BattlegroundsState EndGame(BattlegroundsState state) => state with
    {
        IsActive = false,
        Phase = BattlegroundsPhase.GameOver,
        CurrentOpponentPlayerId = null,
    };

    private static int CalculateTurn(int rawTurn)
    {
        if (rawTurn <= 0)
        {
            return 0;
        }

        return (int)(((long)rawTurn + 1) / 2);
    }

    private static bool IsCombatTransition(EntityMutation mutation)
    {
        var tag = (int)mutation.Tag;
        return (tag is BattlegroundsCompatibilityTags.Setup or
                   BattlegroundsCompatibilityTags.CombatSetup) &&
               mutation.PreviousValue == 1 &&
               mutation.Value == 0;
    }

    private static bool IsTerminalPlayState(int playState) => playState is
        (int)HearthstonePlayState.Won or
        (int)HearthstonePlayState.Lost or
        (int)HearthstonePlayState.Tied or
        (int)HearthstonePlayState.Disconnected or
        (int)HearthstonePlayState.Conceded;

    private static bool IsAlivePlayState(int playState) => playState is not
        (int)HearthstonePlayState.Lost and not
        (int)HearthstonePlayState.Tied and not
        (int)HearthstonePlayState.Disconnected and not
        (int)HearthstonePlayState.Conceded;

    private static bool HasTag(EntitySnapshot entity, GameTag tag) =>
        entity.Tags.ContainsKey(tag);

    private static bool HasAnyTag(EntitySnapshot entity, GameTag first, GameTag second) =>
        HasTag(entity, first) || HasTag(entity, second);

    private static int? PositiveOrNull(int? value) => value > 0 ? value : null;

    // Values verified against HearthDb Enums.cs at revision
    // 37981c80d9b8c164db8cdb5cfa18c708c32d111e on 2026-08-13.
    private enum HearthstonePlayState
    {
        Won = 4,
        Lost = 5,
        Tied = 6,
        Disconnected = 7,
        Conceded = 8,
    }
}
