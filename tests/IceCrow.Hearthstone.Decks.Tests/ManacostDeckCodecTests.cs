using System.Diagnostics;
using IceCrow.Hearthstone.Data;
using Xunit;
using Xunit.Abstractions;

namespace IceCrow.Hearthstone.Decks.Tests;

public sealed class ManacostDeckCodecTests(ITestOutputHelper output)
{
    [Fact]
    public void KnownFixtureDecodesAndRoundTripsCanonically()
    {
        var codec = new ManacostDeckCodec();
        var result = codec.Decode("AAEBAQcBBAMBAgMAAA==");

        Assert.True(result.Success);
        Assert.Equal(DeckFormat.Wild, result.Deck?.Format);
        Assert.Equal("AAEBAQcBBAMBAgMAAA==", codec.Encode(result.Deck!));
    }

    [Fact]
    public void SideboardsStayInsideThePackageAdapter()
    {
        var codec = new ManacostDeckCodec();
        var result = codec.Decode("AAEBAQcBBAMBAgMAAQEF/cQFAAA=");

        var sideboard = Assert.Single(result.Deck!.SideboardCards);
        Assert.Equal(new DeckSideboardCard(5, 1, 90749), sideboard);
        Assert.Equal("AAEBAQcBBAMBAgMAAQEF/cQFAAA=", codec.Encode(result.Deck));
    }

    [Fact]
    public void ClipboardExportUsesTheOfflineCardDatabaseResolver()
    {
        var database = new InMemoryCardDatabase();
        database.Replace(new HearthstoneDataSnapshot(
            new HearthstoneDataVersion(1, "test", null, new string('0', 64), DateTimeOffset.UnixEpoch),
            [new CardDefinition(1, "CARD_1", "Known Card", null, null, "spell", 3, null, [], true, false, null, CardImageInfo.Empty)],
            []));
        var codec = new ManacostDeckCodec(database);
        var deck = new DeckDefinition(DeckFormat.Wild, [7], [new DeckCard(1, 2)]);

        var export = codec.FormatExport(deck, new DeckExportMetadata("IceCrow Deck", []));
        var parsed = codec.ParseExport(export);

        Assert.Contains("# 2x (3) Known Card", export, StringComparison.Ordinal);
        Assert.True(parsed.Success);
        Assert.Equal("IceCrow Deck", parsed.Export?.Metadata.Name);
    }

    [Fact]
    public void InvalidDeckstringsReturnStablePackageErrorCodes()
    {
        var result = new ManacostDeckCodec().Decode("not base64");

        Assert.False(result.Success);
        Assert.Equal("invalid_base64", result.ErrorCode);
    }

    [Fact]
    [Trait("Category", "PerformanceDiagnostic")]
    public void DeckstringCodecProducesAnObservableOfflineBaseline()
    {
        const string deckstring = "AAEBAQcBBAMBAgMAAQEF/cQFAAA=";
        var codec = new ManacostDeckCodec();
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 10_000; index++)
        {
            var decoded = codec.Decode(deckstring);
            Assert.True(decoded.Success);
            _ = codec.Encode(decoded.Deck!);
        }

        output.WriteLine("10k decode+encode pairs={0:F1}ms", stopwatch.Elapsed.TotalMilliseconds);
    }
}
