namespace IceCrow.Hearthstone.ClientState;

public interface IClientStateProvider : IAsyncDisposable
{
    ClientStateCapabilities Capabilities { get; }

    ValueTask<ClientStateSnapshot> ReadAsync(
        CancellationToken cancellationToken = default);
}
