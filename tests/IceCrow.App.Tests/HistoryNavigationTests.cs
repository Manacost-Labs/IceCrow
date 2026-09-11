using IceCrow.App.History;
using IceCrow.ProfileSync.History;

namespace IceCrow.App.Tests;

public sealed class HistoryNavigationTests
{
    [Theory]
    [InlineData("matches:standard", HistoryGameMode.Standard)]
    [InlineData("matches:wild", HistoryGameMode.Wild)]
    [InlineData("matches:arena", HistoryGameMode.Arena)]
    [InlineData("matches:battlegrounds", HistoryGameMode.Battlegrounds)]
    public void MatchRouteIncludesOnlyRequestedMode(string routeKey, HistoryGameMode expected)
    {
        var route = HistoryNavigation.Resolve(routeKey);

        Assert.Equal(HistoryPage.Matches, route.Page);
        foreach (var mode in Enum.GetValues<HistoryGameMode>())
        {
            Assert.Equal(mode == expected, HistoryNavigation.Includes(route.Mode, mode));
        }
    }

    [Fact]
    public void AllMatchesRouteIncludesEveryKnownMode()
    {
        var route = HistoryNavigation.Resolve("matches:all");

        Assert.Equal(HistoryPage.Matches, route.Page);
        Assert.All(Enum.GetValues<HistoryGameMode>(), mode =>
            Assert.True(HistoryNavigation.Includes(route.Mode, mode)));
    }
}
