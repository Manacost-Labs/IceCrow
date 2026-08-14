using System.Text;

namespace IceCrow.Hearthstone.Protocol.Tests;

public sealed class PowerLineParserPropertyTests
{
    public const int Seed = 0x1CEC0DE;
    private const int GeneratedCaseCount = 2_000;

    [Fact]
    public void DeterministicMalformedInputsAlwaysProduceAClassifiedResult()
    {
        var parser = new PowerLineParser();
        var random = new Random(Seed);
        var cases = CreateFixedCases().Concat(CreateRandomCases(random, GeneratedCaseCount));
        var index = 0;

        foreach (var input in cases)
        {
            var exception = Record.Exception(() =>
            {
                var result = parser.Parse(input);
                Assert.True(
                    Enum.IsDefined(result.Status),
                    $"Seed={Seed}, Case={index}, Length={input?.Length ?? -1}");
                Assert.Contains(
                    result.Status,
                    new PowerParseStatus[]
                    {
                        PowerParseStatus.Parsed,
                        PowerParseStatus.Ignored,
                        PowerParseStatus.Unknown,
                        PowerParseStatus.Malformed,
                    });
            });

            Assert.Null(exception);
            index++;
        }

        Assert.Equal(GeneratedCaseCount + CreateFixedCases().Count, index);
        Assert.Equal(
            index,
            parser.Context.Parsed +
            parser.Context.Ignored +
            parser.Context.Unknown +
            parser.Context.Malformed);
        Assert.InRange(parser.Context.BlockStack.Count, 0, PowerParserContext.MaximumBlockDepth);
    }

    [Fact]
    public void DeepAndUnbalancedBlockStreamsRemainBounded()
    {
        var parser = new PowerLineParser();
        const string blockStart =
            "BLOCK_START BlockType=TRIGGER Entity=GameEntity EffectCardId= " +
            "EffectIndex=0 Target=0 SubOption=-1";

        for (var index = 0; index < PowerParserContext.MaximumBlockDepth * 4; index++)
        {
            _ = parser.Parse(blockStart);
        }

        for (var index = 0; index < PowerParserContext.MaximumBlockDepth * 6; index++)
        {
            _ = parser.Parse("BLOCK_END");
        }

        Assert.InRange(parser.Context.BlockStack.Count, 0, PowerParserContext.MaximumBlockDepth);
        Assert.Equal(
            PowerParserContext.MaximumBlockDepth * 10,
            parser.Context.Parsed +
            parser.Context.Ignored +
            parser.Context.Malformed +
            parser.Context.Unknown);
    }

    private static IReadOnlyList<string?> CreateFixedCases() =>
    [
        null,
        string.Empty,
        "TAG_CHANGE",
        "TAG_CHANGE Entity= tag= value=",
        "TAG_CHANGE Entity=1 tag=HEALTH",
        "TAG_CHANGE Entity=[name=a=b id=1] tag=HEALTH value=-999999999999999999999",
        "FULL_ENTITY - Creating ID=broken CardID=",
        "FULL_ENTITY - Updating Entity=[name=x id=] CardID=###",
        "BLOCK_START",
        "BLOCK_START BlockType=TRIGGER Entity=[] EffectCardId= EffectIndex=x Target== SubOption=",
        "BLOCK_END trailing-data",
        "tag=ZONE value=PLAY delimiter==[]{}::",
        "\u0000\u0001\ud800\udfff",
        new string('A', PowerLineParser.MaximumInputCharacters),
        new string('A', PowerLineParser.MaximumInputCharacters + 1),
        $"TAG_CHANGE Entity=1 tag={new string('T', 64 * 1024)} value=1",
        $"SHOW_ENTITY - Updating Entity=[name={new string('N', 64 * 1024)} id=7] CardID=CARD_1",
    ];

    private static IEnumerable<string> CreateRandomCases(Random random, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var length = random.Next(0, 4_097);
            yield return (index % 4) switch
            {
                0 => RandomAscii(random, length),
                1 => RandomUnicode(random, length),
                2 => $"TAG_CHANGE Entity={RandomAscii(random, length / 3)} tag={RandomAscii(random, length / 3)} value={RandomAscii(random, length / 3)}",
                _ => $"BLOCK_START BlockType={RandomAscii(random, length / 4)} Entity={RandomUnicode(random, length / 4)} EffectIndex={RandomAscii(random, length / 4)} Target={RandomUnicode(random, length / 4)}",
            };
        }
    }

    private static string RandomAscii(Random random, int length)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 []{}=:_-/\\\t\r\n\0";
        return string.Create(length, random, static (span, state) =>
        {
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = alphabet[state.Next(alphabet.Length)];
            }
        });
    }

    private static string RandomUnicode(Random random, int length)
    {
        var builder = new StringBuilder(length);
        for (var index = 0; index < length; index++)
        {
            builder.Append((char)random.Next(char.MinValue, char.MaxValue + 1));
        }

        return builder.ToString();
    }
}
