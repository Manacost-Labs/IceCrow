namespace IceCrow.Hearthstone.ClientState.Tests;

internal sealed class FakeClientStateProvider : IClientStateProvider
{
    private readonly Queue<Func<CancellationToken, ValueTask<ClientStateSnapshot>>> _reads;
    private Func<CancellationToken, ValueTask<ClientStateSnapshot>>? _lastRead;

    public FakeClientStateProvider(
        ClientStateCapabilities capabilities,
        params Func<CancellationToken, ValueTask<ClientStateSnapshot>>[] reads)
    {
        if (reads.Length == 0)
        {
            throw new ArgumentException("At least one fake read must be supplied.", nameof(reads));
        }

        Capabilities = capabilities;
        _reads = new Queue<Func<CancellationToken, ValueTask<ClientStateSnapshot>>>(reads);
    }

    public ClientStateCapabilities Capabilities { get; }

    public bool IsDisposed { get; private set; }

    public static Func<CancellationToken, ValueTask<ClientStateSnapshot>> Returns(
        ClientStateSnapshot snapshot) =>
        cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(snapshot);
        };

    public static Func<CancellationToken, ValueTask<ClientStateSnapshot>> Throws(
        Exception exception) =>
        _ => ValueTask.FromException<ClientStateSnapshot>(exception);

    public ValueTask<ClientStateSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_reads.TryDequeue(out var read))
        {
            _lastRead = read;
        }

        return (_lastRead ?? throw new InvalidOperationException("The fake has no read result."))(
            cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}
