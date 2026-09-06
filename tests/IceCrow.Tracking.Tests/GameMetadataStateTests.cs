using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Tracking.Tests;

public sealed class GameMetadataStateTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("GT_RANKED", GameMode.Ranked)]
    [InlineData("GT_CASUAL", GameMode.Casual)]
    [InlineData("GT_ARENA", GameMode.Arena)]
    [InlineData("GT_BATTLEGROUNDS", GameMode.Battlegrounds)]
    [InlineData("GT_BATTLEGROUNDS_FRIENDLY", GameMode.Battlegrounds)]
    [InlineData("GT_BATTLEGROUNDS_DUO", GameMode.BattlegroundsDuo)]
    [InlineData("GT_UNKNOWN", GameMode.Unknown)]
    [InlineData("GT_TAVERNBRAWL", GameMode.Other)]
    [InlineData("GT_FUTURE_MODE", GameMode.Other)]
    public void ClassifiesGameTypeTokensWithoutGuessing(string token, GameMode expected)
    {
        var state = GameMetadataState.Empty.Apply(
            new GameMetadataObserved(Timestamp, GameMetadataField.GameType, token));

        Assert.Equal(expected, state.Mode);
        Assert.Equal(token, state.GameTypeToken);
    }

    [Theory]
    [InlineData("FT_STANDARD", ConstructedFormat.Standard)]
    [InlineData("FT_WILD", ConstructedFormat.Wild)]
    [InlineData("FT_CLASSIC", ConstructedFormat.Classic)]
    [InlineData("FT_TWIST", ConstructedFormat.Twist)]
    [InlineData("FT_UNKNOWN", ConstructedFormat.Unknown)]
    public void ClassifiesFormatTokens(string token, ConstructedFormat expected)
    {
        var state = GameMetadataState.Empty.Apply(
            new GameMetadataObserved(Timestamp, GameMetadataField.FormatType, token));

        Assert.Equal(expected, state.Format);
    }

    [Fact]
    public void AccumulatesBuildAndScenarioAndIgnoresNegativeNumbers()
    {
        var state = GameMetadataState.Empty
            .Apply(new GameMetadataObserved(Timestamp, GameMetadataField.BuildNumber, "224857"))
            .Apply(new GameMetadataObserved(Timestamp, GameMetadataField.ScenarioId, "-5"));

        Assert.Equal(224857, state.BuildNumber);
        Assert.Null(state.ScenarioId);
        Assert.Equal(GameMode.Unknown, state.Mode);
        Assert.False(state.IsBattlegrounds);
    }
}
