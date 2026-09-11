using System.IO;
using IceCrow.App.Runtime;
using IceCrow.Hearthstone.Data;
using IceCrow.Hearthstone.Decks;

namespace IceCrow.App.Tests;

public sealed class ActiveDeckRuntimeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "IceCrow.ActiveDeckTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ValidDeckSelectionPersistsAndRestores()
    {
        var states = new List<ActiveDeckState>();
        var code = CreateWildDeckCode();
        using (var runtime = Create(states.Add))
        {
            var activated = await runtime.ActivateAsync("Контроль воин", code, CancellationToken.None);

            Assert.False(activated.IsError);
            Assert.Equal("Контроль воин", runtime.Current?.Name);
            Assert.Equal("wild", runtime.Current?.Format);
        }

        using var reopened = Create(states.Add);
        await reopened.InitializeAsync(CancellationToken.None);

        Assert.Equal("Контроль воин", reopened.Current?.Name);
        Assert.Equal(code, reopened.Current?.Snapshot.DeckCode);
        Assert.Contains(states, static state => state.Message == "Активная колода восстановлена.");
    }

    [Fact]
    public async Task InvalidOrOversizedImportDoesNotReplaceCurrentSelection()
    {
        using var runtime = Create(static _ => { });
        var selected = await runtime.ActivateAsync("Рабочая", CreateWildDeckCode(), CancellationToken.None);
        Assert.NotNull(selected.Selection);

        var invalid = await runtime.ActivateAsync(null, "not a deck", CancellationToken.None);
        var invalidName = await runtime.ActivateAsync("Плохое\nимя", CreateWildDeckCode(), CancellationToken.None);
        var oversized = await runtime.ActivateAsync(
            null,
            new string('A', ActiveDeckRuntime.MaximumImportLength + 1),
            CancellationToken.None);

        Assert.True(invalid.IsError);
        Assert.True(invalidName.IsError);
        Assert.True(oversized.IsError);
        Assert.Equal("Рабочая", runtime.Current?.Name);
        Assert.Equal("Рабочая", invalid.Selection?.Name);
        Assert.Equal("Рабочая", invalidName.Selection?.Name);
        Assert.Equal("Рабочая", oversized.Selection?.Name);
    }

    [Fact]
    public async Task ClearRemovesPersistedSelection()
    {
        using (var runtime = Create(static _ => { }))
        {
            _ = await runtime.ActivateAsync("Колода", CreateWildDeckCode(), CancellationToken.None);
            var cleared = await runtime.ClearAsync(CancellationToken.None);
            Assert.Null(cleared.Selection);
            Assert.Null(runtime.Current);
        }

        using var reopened = Create(static _ => { });
        await reopened.InitializeAsync(CancellationToken.None);
        Assert.Null(reopened.Current);
    }

    [Fact]
    public async Task OversizedPersistedFileIsRejectedBeforeDeserialization()
    {
        var deckDirectory = Path.Combine(_directory, "decks");
        Directory.CreateDirectory(deckDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(deckDirectory, "active.json"),
            new byte[ActiveDeckRuntime.MaximumFileBytes + 1]);
        var states = new List<ActiveDeckState>();
        using var runtime = Create(states.Add);

        await runtime.InitializeAsync(CancellationToken.None);

        Assert.Null(runtime.Current);
        var state = Assert.Single(states);
        Assert.True(state.IsError);
        Assert.Contains("повреждена", state.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private ActiveDeckRuntime Create(Action<ActiveDeckState> onState) => new(
        _directory,
        new ManacostDeckCodec(),
        new InMemoryCardDatabase(),
        onState,
        new FixedTimeProvider(new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero)));

    private static string CreateWildDeckCode()
    {
        var codec = new ManacostDeckCodec();
        return codec.Encode(new DeckDefinition(
            DeckFormat.Wild,
            [7],
            [new DeckCard(1, 2), new DeckCard(3, 1)]));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
