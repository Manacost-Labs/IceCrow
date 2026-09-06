namespace IceCrow.Hearthstone.ClientState.Tests;

public sealed class ClientStateSourceGuardTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ThrowingProviderBecomesNullAndIsCounted()
    {
        var failure = new IOException("simulated memory read failure");
        var source = new ScriptedRatingSource(
            _ => throw failure,
            _ => throw new InvalidOperationException("second failure"));
        var guard = new ClientStateSourceGuard<BattlegroundsRatingSnapshot>(source);

        Assert.Null(await guard.ReadAsync());
        Assert.Null(await guard.ReadAsync());

        Assert.Equal(2, guard.FailureCount);
        Assert.IsType<InvalidOperationException>(guard.LastError);
        Assert.Equal(ClientStateProviderStatus.Disconnected, guard.Status);
    }

    [Fact]
    public async Task SuccessfulReadClearsTheLastErrorAndPassesTheSnapshotThrough()
    {
        var snapshot = new BattlegroundsRatingSnapshot(ObservedAt, 7000, 6500);
        var source = new ScriptedRatingSource(
            _ => throw new IOException("first failure"),
            _ => ValueTask.FromResult<BattlegroundsRatingSnapshot?>(snapshot));
        var guard = new ClientStateSourceGuard<BattlegroundsRatingSnapshot>(source);

        Assert.Null(await guard.ReadAsync());
        var recovered = await guard.ReadAsync();

        Assert.Same(snapshot, recovered);
        Assert.Null(guard.LastError);
        Assert.Equal(1, guard.FailureCount);
        Assert.Equal(ClientStateProviderStatus.Connected, guard.Status);
    }

    [Fact]
    public async Task AbsentClientStateIsNullWithoutCountingAFailure()
    {
        var source = new ScriptedRatingSource(_ => ValueTask.FromResult<BattlegroundsRatingSnapshot?>(null))
        {
            ConfiguredStatus = ClientStateProviderStatus.Unavailable,
        };
        var guard = new ClientStateSourceGuard<BattlegroundsRatingSnapshot>(source);

        Assert.Null(await guard.ReadAsync());

        Assert.Equal(0, guard.FailureCount);
        Assert.Equal(ClientStateProviderStatus.Unavailable, guard.Status);
    }

    [Fact]
    public async Task CallerCancellationEscapesInsteadOfBeingCounted()
    {
        var source = new ScriptedRatingSource(cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<BattlegroundsRatingSnapshot?>(null);
        });
        var guard = new ClientStateSourceGuard<BattlegroundsRatingSnapshot>(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await guard.ReadAsync(cancellation.Token));
        Assert.Equal(0, guard.FailureCount);
    }

    [Fact]
    public void ThrowingStatusIsReportedAsDisconnected()
    {
        var source = new ScriptedRatingSource(_ => ValueTask.FromResult<BattlegroundsRatingSnapshot?>(null))
        {
            ThrowOnStatus = true,
        };
        var guard = new ClientStateSourceGuard<BattlegroundsRatingSnapshot>(source);

        Assert.Equal(ClientStateProviderStatus.Disconnected, guard.Status);
    }

    private sealed class ScriptedRatingSource(
        params Func<CancellationToken, ValueTask<BattlegroundsRatingSnapshot?>>[] reads)
        : IBattlegroundsRatingSource
    {
        private int _index;

        public ClientStateProviderStatus Status =>
            ThrowOnStatus ? throw new InvalidOperationException("status failure") : ConfiguredStatus;

        public ClientStateProviderStatus ConfiguredStatus { get; init; } = ClientStateProviderStatus.Connected;

        public bool ThrowOnStatus { get; init; }

        public ValueTask<BattlegroundsRatingSnapshot?> ReadAsync(CancellationToken cancellationToken = default)
        {
            var read = reads[Math.Min(_index, reads.Length - 1)];
            _index++;
            return read(cancellationToken);
        }
    }
}
