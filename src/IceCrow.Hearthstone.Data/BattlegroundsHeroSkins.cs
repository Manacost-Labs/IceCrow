using System.Text.RegularExpressions;

namespace IceCrow.Hearthstone.Data;

/// <summary>
/// Battlegrounds hero skins append a <c>_SKIN_&lt;variant&gt;</c> suffix to the
/// base hero card id (observed in the 2026-08-16 real client session:
/// <c>BG22_HERO_000_SKIN_E</c> for base <c>BG22_HERO_000</c>; HDT's hero-skin
/// handling was consulted as behavioral reference). Only this documented
/// pattern is normalized — anything else stays unknown.
/// </summary>
public static partial class BattlegroundsHeroSkins
{
    [GeneratedRegex(@"^(?<baseId>.+?)_SKIN_[A-Z0-9]+$")]
    private static partial Regex SkinSuffix();

    public static bool TryGetBaseHeroCardId(string cardId, out string baseHeroCardId)
    {
        ArgumentNullException.ThrowIfNull(cardId);
        var match = SkinSuffix().Match(cardId);
        if (!match.Success)
        {
            baseHeroCardId = string.Empty;
            return false;
        }

        baseHeroCardId = match.Groups["baseId"].Value;
        return true;
    }
}
