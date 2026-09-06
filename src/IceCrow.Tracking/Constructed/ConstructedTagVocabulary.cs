using System.Globalization;
using IceCrow.Hearthstone.Entities;

namespace IceCrow.Tracking.Constructed;

internal enum ConstructedTag
{
    None,
    Controller,
    Zone,
    CardType,
    ZonePosition,
    PlayerId,
    Turn,
    PlayState,
    MulliganState,
    State,
}

/// <summary>
/// The small tag and value vocabulary the Constructed tracker needs, accepted
/// in the same numeric-or-symbolic forms as the entity store but without any
/// dependency on it. Unknown tags and unknown values are ignored, except that
/// an unrecognised symbolic <c>CARDTYPE</c> token maps to
/// <see cref="OtherCardType"/> so a new client card type (for example a
/// location) still counts as a real card rather than as no card type at all.
/// </summary>
internal static class ConstructedTagVocabulary
{
    public const int OtherCardType = 1 << 16;

    public static ConstructedTag ParseTag(string rawTag)
    {
        if (int.TryParse(rawTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return (GameTag)numeric switch
            {
                GameTag.Controller => ConstructedTag.Controller,
                GameTag.Zone => ConstructedTag.Zone,
                GameTag.CardType => ConstructedTag.CardType,
                GameTag.ZonePosition => ConstructedTag.ZonePosition,
                GameTag.PlayerId => ConstructedTag.PlayerId,
                GameTag.Turn => ConstructedTag.Turn,
                GameTag.PlayState => ConstructedTag.PlayState,
                GameTag.MulliganState => ConstructedTag.MulliganState,
                GameTag.State => ConstructedTag.State,
                _ => ConstructedTag.None,
            };
        }

        return rawTag switch
        {
            "CONTROLLER" => ConstructedTag.Controller,
            "ZONE" => ConstructedTag.Zone,
            "CARDTYPE" => ConstructedTag.CardType,
            "ZONE_POSITION" => ConstructedTag.ZonePosition,
            "PLAYER_ID" => ConstructedTag.PlayerId,
            "TURN" => ConstructedTag.Turn,
            "PLAYSTATE" => ConstructedTag.PlayState,
            "MULLIGAN_STATE" => ConstructedTag.MulliganState,
            "STATE" => ConstructedTag.State,
            _ => ConstructedTag.None,
        };
    }

    public static bool TryParseValue(ConstructedTag tag, string rawValue, out int value)
    {
        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return value >= 0;
        }

        value = tag switch
        {
            ConstructedTag.Zone => ParseZone(rawValue),
            ConstructedTag.CardType => ParseCardType(rawValue),
            ConstructedTag.PlayState => ParsePlayState(rawValue),
            ConstructedTag.MulliganState => ParseMulliganState(rawValue),
            ConstructedTag.State => ParseLifecycleState(rawValue),
            _ => -1,
        };
        return value >= 0;
    }

    private static int ParseZone(string rawValue) => rawValue switch
    {
        "INVALID" => (int)Zone.Invalid,
        "PLAY" => (int)Zone.Play,
        "DECK" => (int)Zone.Deck,
        "HAND" => (int)Zone.Hand,
        "GRAVEYARD" => (int)Zone.Graveyard,
        "REMOVEDFROMGAME" => (int)Zone.RemovedFromGame,
        "SETASIDE" => (int)Zone.SetAside,
        "SECRET" => (int)Zone.Secret,
        _ => -1,
    };

    private static int ParseCardType(string rawValue) => rawValue switch
    {
        "INVALID" => (int)CardType.Invalid,
        "GAME" => (int)CardType.Game,
        "PLAYER" => (int)CardType.Player,
        "HERO" => (int)CardType.Hero,
        "MINION" => (int)CardType.Minion,
        "SPELL" => (int)CardType.Spell,
        "ENCHANTMENT" => (int)CardType.Enchantment,
        "WEAPON" => (int)CardType.Weapon,
        "ITEM" => (int)CardType.Item,
        "TOKEN" => (int)CardType.Token,
        "HERO_POWER" => (int)CardType.HeroPower,
        "BLANK" => (int)CardType.Blank,
        "GAME_MODE_BUTTON" => (int)CardType.GameModeButton,
        { Length: > 0 } => OtherCardType,
        _ => -1,
    };

    private static int ParsePlayState(string rawValue) => rawValue switch
    {
        "INVALID" => (int)GamePlayState.Invalid,
        "PLAYING" => (int)GamePlayState.Playing,
        "WINNING" => (int)GamePlayState.Winning,
        "LOSING" => (int)GamePlayState.Losing,
        "WON" => (int)GamePlayState.Won,
        "LOST" => (int)GamePlayState.Lost,
        "TIED" => (int)GamePlayState.Tied,
        "DISCONNECTED" => (int)GamePlayState.Disconnected,
        "CONCEDED" => (int)GamePlayState.Conceded,
        _ => -1,
    };

    private static int ParseMulliganState(string rawValue) => rawValue switch
    {
        "INVALID" => (int)MulliganState.Invalid,
        "INPUT" => (int)MulliganState.Input,
        "DEALING" => (int)MulliganState.Dealing,
        "WAITING" => (int)MulliganState.Waiting,
        "DONE" => (int)MulliganState.Done,
        "REFRESHING" => (int)MulliganState.Refreshing,
        "PREREFRESHING" => (int)MulliganState.PreRefreshing,
        _ => -1,
    };

    private static int ParseLifecycleState(string rawValue) => rawValue switch
    {
        "INVALID" => (int)GameLifecycleState.Invalid,
        "LOADING" => (int)GameLifecycleState.Loading,
        "RUNNING" => (int)GameLifecycleState.Running,
        "COMPLETE" => (int)GameLifecycleState.Complete,
        _ => -1,
    };
}
