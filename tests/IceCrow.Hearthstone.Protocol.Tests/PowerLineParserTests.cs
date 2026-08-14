using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Hearthstone.Protocol.Tests.Fixtures;

namespace IceCrow.Hearthstone.Protocol.Tests;

public sealed class PowerLineParserTests
{
    [Theory]
    [MemberData(
        nameof(PowerProtocolFixtures.SupportedSingleLineEvents),
        MemberType = typeof(PowerProtocolFixtures))]
    public void ParsesSupportedSingleLineEvents(string content, GameEvent expected)
    {
        var parser = new PowerLineParser();

        var result = parser.Parse(content, PowerProtocolFixtures.Timestamp);

        Assert.Equal(PowerParseStatus.Parsed, result.Status);
        Assert.Equal(expected, result.Event);
        Assert.Null(result.Diagnostic);
    }

    [Fact]
    public void PreservesNestedBlockStackAndParentRelationships()
    {
        var parser = new PowerLineParser();
        const string outerLine =
            "BLOCK_START BlockType=PLAY Entity=[name=I Know a Guy id=59 zone=HAND zonePos=2 cardId=CFM_940 player=2] " +
            "EffectCardId= EffectIndex=0 Target=0 SubOption=0";
        const string innerLine =
            "BLOCK_START BlockType=TRIGGER Entity=GameEntity EffectCardId=CS2_034 " +
            "EffectIndex=-1 Target=59 SubOption=-1 TriggerKeyword=DEATHRATTLE";

        var outerResult = parser.Parse(outerLine, PowerProtocolFixtures.Timestamp);
        var innerResult = parser.Parse(innerLine, PowerProtocolFixtures.Timestamp);

        Assert.True(outerResult.Status == PowerParseStatus.Parsed, outerResult.Diagnostic);
        Assert.True(innerResult.Status == PowerParseStatus.Parsed, innerResult.Diagnostic);
        var outer = Assert.IsType<BlockStarted>(outerResult.Event).Block;
        var inner = Assert.IsType<BlockStarted>(innerResult.Event).Block;
        Assert.Equal(
            new PowerBlock(
                Id: 0,
                ParentId: null,
                Depth: 0,
                Type: "PLAY",
                EntityId: 59,
                EntityName: "I Know a Guy",
                EffectCardId: string.Empty,
                Target: "0",
                SubOption: 0,
                TriggerKeyword: null),
            outer);
        Assert.Equal(
            new PowerBlock(
                Id: 1,
                ParentId: 0,
                Depth: 1,
                Type: "TRIGGER",
                EntityId: null,
                EntityName: "GameEntity",
                EffectCardId: "CS2_034",
                Target: "59",
                SubOption: -1,
                TriggerKeyword: "DEATHRATTLE"),
            inner);
        Assert.Equal([outer, inner], parser.Context.BlockStack);

        var innerEnd = parser.Parse("BLOCK_END", PowerProtocolFixtures.Timestamp);
        Assert.Equal(inner, Assert.IsType<BlockEnded>(innerEnd.Event).Block);
        Assert.Equal(outer, parser.Context.CurrentBlock);

        var outerEnd = parser.Parse("BLOCK_END", PowerProtocolFixtures.Timestamp);
        Assert.Equal(outer, Assert.IsType<BlockEnded>(outerEnd.Event).Block);
        Assert.Null(parser.Context.CurrentBlock);
        Assert.Empty(parser.Context.BlockStack);
    }

    [Fact]
    public void AssociatesIndentedCreationTagWithCurrentFullEntity()
    {
        var parser = new PowerLineParser();
        _ = parser.Parse("FULL_ENTITY - Creating ID=221 CardID=");

        var result = parser.Parse("tag=ZONE value=DECK", PowerProtocolFixtures.Timestamp);

        Assert.Equal(
            new RawTagChanged(
                PowerProtocolFixtures.Timestamp,
                BlockId: null,
                EntityId: 221,
                EntityName: null,
                Tag: "ZONE",
                Value: "DECK",
                IsCreationTag: true),
            result.Event);
        Assert.Equal(221, parser.Context.CurrentCreationEntityId);
    }

    [Fact]
    public void CreateGameClearsEntityAndBlockContext()
    {
        var parser = new PowerLineParser();
        _ = parser.Parse("FULL_ENTITY - Creating ID=221 CardID=");
        _ = parser.Parse(
            "BLOCK_START BlockType=TRIGGER Entity=GameEntity EffectCardId= " +
            "EffectIndex=0 Target=0 SubOption=-1");

        var result = parser.Parse(
            "PowerTaskList.DebugPrintPower() - CREATE_GAME",
            PowerProtocolFixtures.Timestamp);

        Assert.Equal(new GameCreated(PowerProtocolFixtures.Timestamp), result.Event);
        Assert.Null(parser.Context.CurrentEntityId);
        Assert.Null(parser.Context.CurrentCreationEntityId);
        Assert.Empty(parser.Context.BlockStack);
    }

    [Fact]
    public void MalformedRecognizedLineDoesNotThrowAndDeduplicatesDiagnostic()
    {
        var parser = new PowerLineParser();

        var first = parser.Parse("GameEntity EntityID=not-a-number");
        var repeated = parser.Parse("GameEntity EntityID=still-not-a-number");

        Assert.Equal(PowerParseStatus.Malformed, first.Status);
        Assert.NotNull(first.Diagnostic);
        Assert.Null(first.Event);
        Assert.Equal(PowerParseStatus.Malformed, repeated.Status);
        Assert.Null(repeated.Diagnostic);
        Assert.Equal(2, parser.Context.Malformed);
    }

    [Fact]
    public void MalformedReplacementDeclarationInvalidatesEntityContext()
    {
        var parser = new PowerLineParser();
        _ = parser.Parse("GameEntity EntityID=7");

        var malformed = parser.Parse("GameEntity EntityID=not-a-number");
        var orphanTag = parser.Parse("tag=ZONE value=DECK");

        Assert.Equal(PowerParseStatus.Malformed, malformed.Status);
        Assert.Null(parser.Context.CurrentEntityId);
        Assert.Equal(PowerParseStatus.Malformed, orphanTag.Status);
        Assert.Null(orphanTag.Event);
    }

    [Fact]
    public void MalformedLineInvalidatesCreationContext()
    {
        var parser = new PowerLineParser();
        _ = parser.Parse("FULL_ENTITY - Creating ID=221 CardID=");

        var malformed = parser.Parse("BLOCK_END unexpected");
        var orphanTag = parser.Parse("tag=ZONE value=DECK");

        Assert.Equal(PowerParseStatus.Malformed, malformed.Status);
        Assert.Null(parser.Context.CurrentCreationEntityId);
        Assert.Equal(PowerParseStatus.Malformed, orphanTag.Status);
        Assert.Null(orphanTag.Event);
    }

    [Fact]
    public void UnknownLineProducesExplicitUnknownEvent()
    {
        var parser = new PowerLineParser();

        var result = parser.Parse(
            "PowerTaskList.DebugPrintPower() - META_DATA - Meta=TARGET Data=0 InfoCount=0",
            PowerProtocolFixtures.Timestamp);

        Assert.Equal(PowerParseStatus.Unknown, result.Status);
        Assert.Equal(
            new UnknownPowerEvent(
                PowerProtocolFixtures.Timestamp,
                BlockId: null,
                Content: "META_DATA - Meta=TARGET Data=0 InfoCount=0"),
            result.Event);
        Assert.Equal(1, parser.Context.Unknown);
    }

    [Fact]
    public void EndCurrentTaskListProducesExplicitIgnoredResult()
    {
        var parser = new PowerLineParser();

        var result = parser.Parse("PowerProcessor.EndCurrentTaskList() - m_currentTaskList=3");

        Assert.Equal(PowerParseStatus.Ignored, result.Status);
        Assert.Null(result.Event);
        Assert.Equal(1, parser.Context.Ignored);
    }

    [Fact]
    public void RejectsInputAboveParserLimitBeforeRegexProcessing()
    {
        var parser = new PowerLineParser();
        _ = parser.Parse("GameEntity EntityID=7");
        var oversized = new string('A', PowerLineParser.MaximumInputCharacters + 1);

        var result = parser.Parse(oversized);
        var orphanTag = parser.Parse("tag=ZONE value=DECK");

        Assert.Equal(PowerParseStatus.Malformed, result.Status);
        Assert.Contains("exceeds", result.Diagnostic, StringComparison.Ordinal);
        Assert.Null(parser.Context.CurrentEntityId);
        Assert.Equal(PowerParseStatus.Malformed, orphanTag.Status);
    }

    [Fact]
    public void NullInputInvalidatesEntityContext()
    {
        var parser = new PowerLineParser();
        _ = parser.Parse("GameEntity EntityID=7");

        var result = parser.Parse(null);
        var orphanTag = parser.Parse("tag=ZONE value=DECK");

        Assert.Equal(PowerParseStatus.Malformed, result.Status);
        Assert.Null(parser.Context.CurrentEntityId);
        Assert.Equal(PowerParseStatus.Malformed, orphanTag.Status);
    }

    [Fact]
    public void RejectsUnbalancedBlockEndWithoutMutatingStack()
    {
        var parser = new PowerLineParser();

        var result = parser.Parse("BLOCK_END");

        Assert.Equal(PowerParseStatus.Malformed, result.Status);
        Assert.Empty(parser.Context.BlockStack);
    }

    [Fact]
    public void RejectedNestedBlockDoesNotPopAcceptedParent()
    {
        var parser = new PowerLineParser();
        const string outerLine =
            "BLOCK_START BlockType=TRIGGER Entity=GameEntity EffectCardId= " +
            "EffectIndex=0 Target=0 SubOption=-1";
        var outer = Assert.IsType<BlockStarted>(parser.Parse(outerLine).Event).Block;

        var malformedInner = parser.Parse("BLOCK_START malformed");
        var rejectedInnerEnd = parser.Parse("BLOCK_END");

        Assert.Equal(PowerParseStatus.Malformed, malformedInner.Status);
        Assert.Equal(PowerParseStatus.Ignored, rejectedInnerEnd.Status);
        Assert.Equal(outer, parser.Context.CurrentBlock);
        Assert.Equal([outer], parser.Context.BlockStack);

        var outerEnd = parser.Parse("BLOCK_END");
        Assert.Equal(outer, Assert.IsType<BlockEnded>(outerEnd.Event).Block);
        Assert.Empty(parser.Context.BlockStack);
    }

    [Fact]
    public void CapsBlockNestingDepth()
    {
        var parser = new PowerLineParser();
        const string line =
            "BLOCK_START BlockType=TRIGGER Entity=GameEntity EffectCardId= " +
            "EffectIndex=0 Target=0 SubOption=-1";

        for (var index = 0; index < PowerParserContext.MaximumBlockDepth; index++)
        {
            var result = parser.Parse(line);
            Assert.True(result.Status == PowerParseStatus.Parsed, result.Diagnostic);
        }

        var overflow = parser.Parse(line);

        Assert.Equal(PowerParseStatus.Malformed, overflow.Status);
        Assert.Equal(PowerParserContext.MaximumBlockDepth, parser.Context.BlockStack.Count);
    }

    [Fact]
    public void DiagnosticsCountersClassifyEachLineOnce()
    {
        var parser = new PowerLineParser();

        _ = parser.Parse("GameEntity EntityID=1");
        _ = parser.Parse(string.Empty);
        _ = parser.Parse("GameEntity EntityID=invalid");
        _ = parser.Parse("SOMETHING_NEW value=1");

        Assert.Equal(1, parser.Context.Parsed);
        Assert.Equal(1, parser.Context.Ignored);
        Assert.Equal(1, parser.Context.Malformed);
        Assert.Equal(1, parser.Context.Unknown);
    }
}
