namespace IceCrow.Hearthstone.Protocol.Events;

/// <summary>
/// One <c>GameState.DebugPrintGame()</c> metadata line (build, game type,
/// format, scenario). The value is the raw client token, for example
/// <c>GT_RANKED</c> or <c>FT_STANDARD</c>; interpretation belongs to tracking.
/// </summary>
public sealed record GameMetadataObserved(
    DateTimeOffset Timestamp,
    GameMetadataField Field,
    string Value) : GameEvent(Timestamp, BlockId: null)
{
    public const int MaximumValueLength = 64;
}
