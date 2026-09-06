using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Hearthstone.Protocol.Tests.Fixtures;

namespace IceCrow.Hearthstone.Protocol.Tests;

public sealed class GameMetadataParsingTests
{
    [Fact]
    public void PlayerIdentityLinesAreIgnoredWithoutRetainingTheName()
    {
        var parser = new PowerLineParser();

        var result = parser.Parse(
            "GameState.DebugPrintGame() - PlayerID=1, PlayerName=Example#1234",
            PowerProtocolFixtures.Timestamp);

        Assert.Equal(PowerParseStatus.Ignored, result.Status);
        Assert.Null(result.Event);
        Assert.Equal(1, parser.Context.Ignored);
    }

    [Fact]
    public void UnknownMetadataKeysStayVisibleAsUnknownEvents()
    {
        var parser = new PowerLineParser();

        var result = parser.Parse(
            "GameState.DebugPrintGame() - FutureField=42",
            PowerProtocolFixtures.Timestamp);

        Assert.Equal(PowerParseStatus.Unknown, result.Status);
        var unknown = Assert.IsType<UnknownPowerEvent>(result.Event);
        Assert.Equal("FutureField=42", unknown.Content);
    }

    [Theory]
    [InlineData("GameState.DebugPrintGame() - BuildNumber=latest")]
    [InlineData("GameState.DebugPrintGame() - ScenarioID=")]
    public void NumericFieldsRejectNonNumericValues(string line)
    {
        var parser = new PowerLineParser();

        var result = parser.Parse(line, PowerProtocolFixtures.Timestamp);

        Assert.Equal(PowerParseStatus.Malformed, result.Status);
        Assert.Null(result.Event);
    }

    [Fact]
    public void OverlongValuesAreMalformedNotRetained()
    {
        var parser = new PowerLineParser();
        var value = new string('A', GameMetadataObserved.MaximumValueLength + 1);

        var result = parser.Parse(
            $"GameState.DebugPrintGame() - GameType={value}",
            PowerProtocolFixtures.Timestamp);

        Assert.Equal(PowerParseStatus.Malformed, result.Status);
    }

    [Fact]
    public void MetadataLinesDoNotDisturbBlockOrEntityContext()
    {
        var parser = new PowerLineParser();
        _ = parser.Parse(
            "BLOCK_START BlockType=PLAY Entity=12 EffectCardId= EffectIndex=0 Target=0",
            PowerProtocolFixtures.Timestamp);

        var result = parser.Parse(
            "GameState.DebugPrintGame() - GameType=GT_BATTLEGROUNDS",
            PowerProtocolFixtures.Timestamp);

        var metadata = Assert.IsType<GameMetadataObserved>(result.Event);
        Assert.Null(metadata.BlockId);
        Assert.NotNull(parser.Context.CurrentBlock);
    }
}
