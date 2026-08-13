using System.Globalization;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Hearthstone.Entities;

public static class EntityStoreReducer
{
    public static EntityMutation? Apply(EntityStore store, GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(gameEvent);

        return gameEvent switch
        {
            GameEntityDeclared declared => ApplyGameEntity(store, declared),
            PlayerEntityDeclared declared => ApplyPlayerEntity(store, declared),
            EntityCreated created => ApplyCreatedEntity(store, created),
            EntityRevealed revealed => ApplyUpdatedEntity(
                store,
                revealed.EntityId,
                revealed.EntityName,
                revealed.CardId),
            EntityChanged changed => ApplyUpdatedEntity(
                store,
                changed.EntityId,
                changed.EntityName,
                changed.CardId),
            RawTagChanged tagChanged => ApplyTagChange(store, tagChanged),
            _ => null,
        };
    }

    private static EntityMutation? ApplyGameEntity(
        EntityStore store,
        GameEntityDeclared declared)
    {
        _ = store.GetOrCreate(declared.EntityId);
        return null;
    }

    private static EntityMutation? ApplyPlayerEntity(
        EntityStore store,
        PlayerEntityDeclared declared)
    {
        var entity = store.GetOrCreate(declared.EntityId);
        return entity.ApplyTag(GameTag.PlayerId, declared.PlayerId);
    }

    private static EntityMutation? ApplyCreatedEntity(
        EntityStore store,
        EntityCreated created)
    {
        var entity = store.GetOrCreate(created.EntityId);
        entity.CardId = NullIfEmpty(created.CardId);
        return null;
    }

    private static EntityMutation? ApplyUpdatedEntity(
        EntityStore store,
        int? entityId,
        string? entityName,
        string cardId)
    {
        if (entityId is not int id)
        {
            return null;
        }

        var entity = store.GetOrCreate(id);
        entity.Name = NullIfEmpty(entityName);
        entity.CardId = NullIfEmpty(cardId);
        return null;
    }

    private static EntityMutation? ApplyTagChange(EntityStore store, RawTagChanged tagChanged)
    {
        if (tagChanged.EntityId is not int entityId ||
            !TryParseTag(tagChanged.Tag, out var tag) ||
            !TryParseTagValue(tag, tagChanged.Value, out var value))
        {
            return null;
        }

        return store.GetOrCreate(entityId).ApplyTag(tag, value);
    }

    private static bool TryParseTag(string rawTag, out GameTag tag)
    {
        if (int.TryParse(
                rawTag,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var numericTag))
        {
            tag = (GameTag)numericTag;
            return true;
        }

        tag = rawTag switch
        {
            "PLAYSTATE" => GameTag.PlayState,
            "TURN" => GameTag.Turn,
            "CURRENT_PLAYER" => GameTag.CurrentPlayer,
            "HERO_ENTITY" => GameTag.HeroEntity,
            "PLAYER_ID" => GameTag.PlayerId,
            "DAMAGE" => GameTag.Damage,
            "HEALTH" => GameTag.Health,
            "ATK" => GameTag.Attack,
            "ZONE" => GameTag.Zone,
            "CONTROLLER" => GameTag.Controller,
            "CARDTYPE" => GameTag.CardType,
            "ARMOR" => GameTag.Armor,
            "ZONE_POSITION" => GameTag.ZonePosition,
            "NEXT_OPPONENT_PLAYER_ID" => GameTag.NextOpponentPlayerId,
            "PLAYER_TECH_LEVEL" => GameTag.PlayerTechLevel,
            "PLAYER_TRIPLES" => GameTag.PlayerTriples,
            _ => default,
        };
        return tag != default || rawTag == "0";
    }

    private static bool TryParseTagValue(GameTag tag, string rawValue, out int value)
    {
        if (int.TryParse(
                rawValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value))
        {
            return true;
        }

        if (tag == GameTag.Zone && TryParseZone(rawValue, out var zone))
        {
            value = (int)zone;
            return true;
        }

        if (tag == GameTag.CardType && TryParseCardType(rawValue, out var cardType))
        {
            value = (int)cardType;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryParseZone(string rawValue, out Zone zone)
    {
        zone = rawValue switch
        {
            "INVALID" => Zone.Invalid,
            "PLAY" => Zone.Play,
            "DECK" => Zone.Deck,
            "HAND" => Zone.Hand,
            "GRAVEYARD" => Zone.Graveyard,
            "REMOVEDFROMGAME" => Zone.RemovedFromGame,
            "SETASIDE" => Zone.SetAside,
            "SECRET" => Zone.Secret,
            _ => default,
        };
        return zone != default || rawValue == "INVALID";
    }

    private static bool TryParseCardType(string rawValue, out CardType cardType)
    {
        cardType = rawValue switch
        {
            "INVALID" => CardType.Invalid,
            "GAME" => CardType.Game,
            "PLAYER" => CardType.Player,
            "HERO" => CardType.Hero,
            "MINION" => CardType.Minion,
            "SPELL" => CardType.Spell,
            "ENCHANTMENT" => CardType.Enchantment,
            "WEAPON" => CardType.Weapon,
            "ITEM" => CardType.Item,
            "TOKEN" => CardType.Token,
            "HERO_POWER" => CardType.HeroPower,
            "BLANK" => CardType.Blank,
            "GAME_MODE_BUTTON" => CardType.GameModeButton,
            _ => default,
        };
        return cardType != default || rawValue == "INVALID";
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
