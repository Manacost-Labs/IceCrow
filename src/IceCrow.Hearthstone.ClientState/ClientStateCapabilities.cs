namespace IceCrow.Hearthstone.ClientState;

[Flags]
public enum ClientStateCapabilities
{
    None = 0,
    BattlegroundsMode = 1 << 0,
    HoveredOpponent = 1 << 1,
    Choices = 1 << 2,
}
