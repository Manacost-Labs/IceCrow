namespace IceCrow.Hearthstone.ClientState.Tests;

public sealed class ArenaWatchPolicyTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WatcherSleepsWhenClientStateIsAbsent()
    {
        Assert.Null(ArenaWatchPolicy.Default.NextInterval(null));
    }

    [Fact]
    public void WatcherSleepsWhileARunIsPlayedOrComplete()
    {
        Assert.Null(ArenaWatchPolicy.Default.NextInterval(Arena(isDrafting: false, isRunComplete: false)));
        Assert.Null(ArenaWatchPolicy.Default.NextInterval(Arena(isDrafting: false, isRunComplete: true)));
    }

    [Fact]
    public void DefaultDraftIntervalIs750Milliseconds()
    {
        var interval = ArenaWatchPolicy.Default.NextInterval(Arena(isDrafting: true, isRunComplete: null));

        Assert.Equal(TimeSpan.FromMilliseconds(750), interval);
        Assert.Equal(ArenaWatchPolicy.DraftPollingInterval, ArenaWatchPolicy.Default.EffectiveDraftPollingInterval);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(499)]
    [InlineData(500)]
    [InlineData(750)]
    [InlineData(1000)]
    [InlineData(1001)]
    [InlineData(60_000)]
    public void DraftIntervalIsClampedBetween500And1000Milliseconds(int requestedMilliseconds)
    {
        var policy = new ArenaWatchPolicy(TimeSpan.FromMilliseconds(requestedMilliseconds));

        var interval = policy.NextInterval(Arena(isDrafting: true, isRunComplete: null));

        Assert.NotNull(interval);
        Assert.InRange(interval.Value, ArenaWatchPolicy.MinimumDraftPollingInterval, ArenaWatchPolicy.MaximumDraftPollingInterval);
        Assert.Null(policy.NextInterval(Arena(isDrafting: false, isRunComplete: null)));
    }

    private static ArenaClientSnapshot Arena(bool isDrafting, bool? isRunComplete) =>
        new(ObservedAt, "run", isDrafting, 0, 0, isRunComplete, null, [], [], null, null);
}
