namespace IceCrow.Hearthstone.ClientState.Tests;

public sealed class ClientStateCoordinatorTests
{
    private static readonly DateTimeOffset FirstTime =
        new(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CapabilitiesCanRepresentPartialAvailability()
    {
        var capabilities =
            ClientStateCapabilities.BattlegroundsMode |
            ClientStateCapabilities.HoveredOpponent |
            ClientStateCapabilities.Choices;

        Assert.True(capabilities.HasFlag(ClientStateCapabilities.BattlegroundsMode));
        Assert.True(capabilities.HasFlag(ClientStateCapabilities.HoveredOpponent));
        Assert.True(capabilities.HasFlag(ClientStateCapabilities.Choices));
        Assert.False(capabilities.HasFlag((ClientStateCapabilities)(1 << 8)));
    }

    [Fact]
    public void SnapshotsOwnChoiceCollectionsAndSupportSemanticEquality()
    {
        var source = new[] { "BG27_HERO_001", "BG27_HERO_002" };
        var first = Connected(FirstTime, 5, source);
        source[0] = "MUTATED";
        var second = Connected(FirstTime.AddSeconds(1), 5, ["BG27_HERO_001", "BG27_HERO_002"]);

        Assert.Equal("BG27_HERO_001", first.Battlegrounds!.Choice!.CardIds[0]);
        Assert.NotEqual(first, second);
        Assert.True(first.SemanticallyEquals(second));
    }

    [Fact]
    public async Task ProcessUnavailableIsPublishedOnce()
    {
        var unavailable = ClientStateSnapshot.WithoutClientState(
            FirstTime,
            ClientStateProviderStatus.Unavailable);
        var provider = new FakeClientStateProvider(
            ClientStateCapabilities.HoveredOpponent,
            FakeClientStateProvider.Returns(unavailable),
            FakeClientStateProvider.Returns(ClientStateSnapshot.WithoutClientState(
                FirstTime.AddSeconds(1),
                ClientStateProviderStatus.Unavailable)));
        await using var coordinator = new ClientStateCoordinator(provider);

        var first = await coordinator.RefreshAsync();
        var duplicate = await coordinator.RefreshAsync();

        Assert.NotNull(first);
        Assert.Equal(ClientStateProviderStatus.Unavailable, first.Current.Status);
        Assert.Null(duplicate);
    }

    [Fact]
    public async Task ProviderRecoversAfterProcessBecomesAvailable()
    {
        var provider = new FakeClientStateProvider(
            AllCapabilities,
            FakeClientStateProvider.Returns(ClientStateSnapshot.WithoutClientState(
                FirstTime,
                ClientStateProviderStatus.Unavailable)),
            FakeClientStateProvider.Returns(Connected(FirstTime.AddSeconds(1), 5, ["A", "B"])));
        await using var coordinator = new ClientStateCoordinator(provider);

        var unavailable = await coordinator.RefreshAsync();
        var connected = await coordinator.RefreshAsync();

        Assert.Equal(ClientStateProviderStatus.Unavailable, unavailable!.Current.Status);
        Assert.Equal(ClientStateProviderStatus.Connected, connected!.Current.Status);
        Assert.True(connected.ProviderStatusChanged);
        Assert.Equal(5, connected.Current.Battlegrounds!.HoveredEntityId);
    }

    [Fact]
    public async Task ProviderRestartDoesNotRetainOldHoverOrChoices()
    {
        var provider = new FakeClientStateProvider(
            AllCapabilities,
            FakeClientStateProvider.Returns(Connected(FirstTime, 5, ["OLD"])),
            FakeClientStateProvider.Returns(ClientStateSnapshot.WithoutClientState(
                FirstTime.AddSeconds(1),
                ClientStateProviderStatus.Disconnected)),
            FakeClientStateProvider.Returns(Connected(FirstTime.AddSeconds(2), 9, ["NEW"])));
        await using var coordinator = new ClientStateCoordinator(provider);

        _ = await coordinator.RefreshAsync();
        var disconnected = await coordinator.RefreshAsync();
        var restarted = await coordinator.RefreshAsync();

        Assert.Null(disconnected!.Current.Battlegrounds);
        Assert.Equal(9, restarted!.Current.Battlegrounds!.HoveredEntityId);
        Assert.Equal(["NEW"], restarted.Current.Battlegrounds.Choice!.CardIds);
    }

    [Fact]
    public async Task PartialProviderKeepsAvailableCapabilitiesIndependent()
    {
        var snapshot = new ClientStateSnapshot(
            FirstTime,
            ClientStateProviderStatus.Partial,
            ClientStateCapabilities.BattlegroundsMode |
            ClientStateCapabilities.HoveredOpponent,
            new BattlegroundsClientState(ClientBattlegroundsMode.Solo, 7));
        var provider = new FakeClientStateProvider(
            AllCapabilities,
            FakeClientStateProvider.Returns(snapshot));
        await using var coordinator = new ClientStateCoordinator(provider);

        var change = await coordinator.RefreshAsync();

        Assert.Equal(ClientStateProviderStatus.Partial, change!.Current.Status);
        Assert.False(change.Current.AvailableCapabilities.HasFlag(ClientStateCapabilities.Choices));
        Assert.Null(change.Current.Battlegrounds!.Choice);
    }

    [Fact]
    public async Task IdenticalSnapshotsWithNewTimestampsAreNotRepublished()
    {
        var provider = new FakeClientStateProvider(
            AllCapabilities,
            FakeClientStateProvider.Returns(Connected(FirstTime, 5, ["A"])),
            FakeClientStateProvider.Returns(Connected(FirstTime.AddMilliseconds(100), 5, ["A"])));
        await using var coordinator = new ClientStateCoordinator(provider);

        Assert.NotNull(await coordinator.RefreshAsync());
        Assert.Null(await coordinator.RefreshAsync());
    }

    [Fact]
    public async Task HoveredOpponentChangeIsClassified()
    {
        var provider = new FakeClientStateProvider(
            AllCapabilities,
            FakeClientStateProvider.Returns(Connected(FirstTime, 5, ["A"])),
            FakeClientStateProvider.Returns(Connected(FirstTime.AddMilliseconds(100), 7, ["A"])));
        await using var coordinator = new ClientStateCoordinator(provider);

        _ = await coordinator.RefreshAsync();
        var change = await coordinator.RefreshAsync();

        Assert.Equal(ClientStateCapabilities.HoveredOpponent, change!.ChangedCapabilities);
        Assert.Equal(7, change.Current.Battlegrounds!.HoveredEntityId);
    }

    [Fact]
    public async Task ChoiceChangeIsClassifiedByOrderedCardIds()
    {
        var provider = new FakeClientStateProvider(
            AllCapabilities,
            FakeClientStateProvider.Returns(Connected(FirstTime, 5, ["A", "B"])),
            FakeClientStateProvider.Returns(Connected(FirstTime.AddMilliseconds(100), 5, ["B", "A"])));
        await using var coordinator = new ClientStateCoordinator(provider);

        _ = await coordinator.RefreshAsync();
        var change = await coordinator.RefreshAsync();

        Assert.Equal(ClientStateCapabilities.Choices, change!.ChangedCapabilities);
        Assert.Equal(["B", "A"], change.Current.Battlegrounds!.Choice!.CardIds);
    }

    [Fact]
    public async Task UnsupportedProviderPublishesNoClientState()
    {
        var provider = new FakeClientStateProvider(
            ClientStateCapabilities.None,
            FakeClientStateProvider.Returns(ClientStateSnapshot.WithoutClientState(
                FirstTime,
                ClientStateProviderStatus.Unsupported)));
        await using var coordinator = new ClientStateCoordinator(provider);

        var change = await coordinator.RefreshAsync();

        Assert.Equal(ClientStateProviderStatus.Unsupported, change!.Current.Status);
        Assert.Equal(ClientStateCapabilities.None, change.Current.AvailableCapabilities);
        Assert.Null(change.Current.Battlegrounds);
    }

    [Fact]
    public async Task UnexpectedProviderFailureIsContainedAndRecoveryIsObservable()
    {
        var failure = new IOException("simulated client read failure");
        var provider = new FakeClientStateProvider(
            AllCapabilities,
            FakeClientStateProvider.Throws(failure),
            FakeClientStateProvider.Returns(Connected(FirstTime.AddSeconds(1), 3, ["A"])));
        await using var coordinator = new ClientStateCoordinator(provider);

        var disconnected = await coordinator.RefreshAsync();
        Assert.Same(failure, coordinator.LastProviderError);
        Assert.Equal(ClientStateProviderStatus.Disconnected, disconnected!.Current.Status);

        var recovered = await coordinator.RefreshAsync();
        Assert.Null(coordinator.LastProviderError);
        Assert.Equal(ClientStateProviderStatus.Connected, recovered!.Current.Status);
    }

    [Fact]
    public async Task CancellationEscapesAsCancellationRatherThanDisconnect()
    {
        var provider = new FakeClientStateProvider(
            AllCapabilities,
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(Connected(FirstTime, 1, ["A"]));
            });
        await using var coordinator = new ClientStateCoordinator(provider);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await coordinator.RefreshAsync(cancellation.Token));
    }

    [Fact]
    public void ChoiceStateRejectsUnreasonableCollections()
    {
        var choices = Enumerable.Range(0, ClientChoiceState.MaximumChoices + 1)
            .Select(index => $"CARD_{index}");

        Assert.Throws<ArgumentOutOfRangeException>(() => new ClientChoiceState(true, choices));
    }

    private const ClientStateCapabilities AllCapabilities =
        ClientStateCapabilities.BattlegroundsMode |
        ClientStateCapabilities.HoveredOpponent |
        ClientStateCapabilities.Choices;

    private static ClientStateSnapshot Connected(
        DateTimeOffset observedAt,
        int? hoveredEntityId,
        IEnumerable<string> choices) =>
        new(
            observedAt,
            ClientStateProviderStatus.Connected,
            AllCapabilities,
            new BattlegroundsClientState(
                ClientBattlegroundsMode.Solo,
                hoveredEntityId,
                new ClientChoiceState(true, choices)));
}
