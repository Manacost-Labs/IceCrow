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
        if (localPlayerId is null &&
            playerId is int observedPlayerId &&
            entity.GetTag(GameTag.CurrentPlayer) > 0)
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
                HeroName = isHero && !string.IsNullOrWhiteSpace(entity.Name)
                    ? entity.Name
                    : existing.HeroName,
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
        if (playerId is int nextOpponentOwner &&
            HasTag(entity, GameTag.NextOpponentPlayerId))
        {
            // HDT TagChangeActions.OnNextOpponentPlayerId at revision
            // d73b3a8220bbc88e836af8cd67a15c44a1fb7021, inspected 2026-08-14,
            // accepts this tag only when it belongs to game.PlayerEntity.
            localPlayerId = nextOpponentOwner;
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
        (int)GamePlayState.Won or
        (int)GamePlayState.Lost or
        (int)GamePlayState.Tied or
        (int)GamePlayState.Disconnected or
        (int)GamePlayState.Conceded;

    private static bool IsAlivePlayState(int playState) => playState is not
        (int)GamePlayState.Lost and not
        (int)GamePlayState.Tied and not
        (int)GamePlayState.Disconnected and not
        (int)GamePlayState.Conceded;

    private static bool HasTag(EntitySnapshot entity, GameTag tag) =>
        entity.Tags.ContainsKey(tag);

    private static bool HasAnyTag(EntitySnapshot entity, GameTag first, GameTag second) =>
        HasTag(entity, first) || HasTag(entity, second);

    private static int? PositiveOrNull(int? value) => value > 0 ? value : null;

}
