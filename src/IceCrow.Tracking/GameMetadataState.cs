using System.Globalization;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Tracking;

/// <summary>
/// The per-game metadata block printed by <c>GameState.DebugPrintGame()</c>,
/// reduced into an immutable value. Tokens are kept verbatim and classified
/// lazily so an unrecognised client value is preserved instead of guessed.
/// </summary>
public sealed record GameMetadataState(
    int? BuildNumber,
    string? GameTypeToken,
    string? FormatTypeToken,
    int? ScenarioId)
{
    public static readonly GameMetadataState Empty = new(null, null, null, null);

    public GameMode Mode => GameTypeToken switch
    {
        null or "GT_UNKNOWN" => GameMode.Unknown,
        "GT_RANKED" => GameMode.Ranked,
        "GT_CASUAL" => GameMode.Casual,
        "GT_ARENA" => GameMode.Arena,
        "GT_BATTLEGROUNDS" or "GT_BATTLEGROUNDS_FRIENDLY" => GameMode.Battlegrounds,
        "GT_BATTLEGROUNDS_DUO" => GameMode.BattlegroundsDuo,
        _ => GameMode.Other,
    };

    public ConstructedFormat Format => FormatTypeToken switch
    {
        "FT_WILD" => ConstructedFormat.Wild,
        "FT_STANDARD" => ConstructedFormat.Standard,
        "FT_CLASSIC" => ConstructedFormat.Classic,
        "FT_TWIST" => ConstructedFormat.Twist,
        _ => ConstructedFormat.Unknown,
    };

    public bool IsBattlegrounds => Mode is GameMode.Battlegrounds or GameMode.BattlegroundsDuo;

    public GameMetadataState Apply(GameMetadataObserved observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        return observed.Field switch
        {
            GameMetadataField.BuildNumber => this with { BuildNumber = ParseInt32(observed.Value) },
            GameMetadataField.GameType => this with { GameTypeToken = observed.Value },
            GameMetadataField.FormatType => this with { FormatTypeToken = observed.Value },
            GameMetadataField.ScenarioId => this with { ScenarioId = ParseInt32(observed.Value) },
            _ => this,
        };
    }

    private static int? ParseInt32(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;
}
