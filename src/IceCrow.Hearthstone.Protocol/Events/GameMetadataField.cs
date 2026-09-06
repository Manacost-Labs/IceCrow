namespace IceCrow.Hearthstone.Protocol.Events;

/// <summary>
/// Metadata fields the client prints once per game through
/// <c>GameState.DebugPrintGame()</c>. Player identity lines from the same
/// block are deliberately not represented: IceCrow never retains player names.
/// </summary>
public enum GameMetadataField
{
    BuildNumber,
    GameType,
    FormatType,
    ScenarioId,
}
