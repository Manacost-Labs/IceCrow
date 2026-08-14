namespace IceCrow.Hearthstone.ClientState;

public enum ClientStateProviderStatus
{
    Unavailable = 0,
    Connected = 1,
    Partial = 2,
    Disconnected = 3,
    Unsupported = 4,
}
