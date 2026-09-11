using System.Text;
using System.Text.Json;
using IceCrow.Hearthstone.ClientState;
using IceCrow.ProfileSync.Collection;

namespace IceCrow.ProfileSync.Tests;

public sealed class HdtCollectionExportSourceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "icecrow-collection-export-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadsSchemaThreeOwnedCountsAndIgnoresPersonalMetadata()
    {
        var path = await WriteExportAsync(
            "hearthstone-collection-20260911-120000.json",
            """
            {
              "exportedAt": "2026-09-11T12:00:00+02:00",
              "source": "Hearthstone Deck Tracker plugin by Manacost",
              "version": 3,
              "user": { "battleTag": "Private#1234", "accountHi": 1, "accountLo": 2 },
              "dust": 9999,
              "cards": [
                { "cardId": "CARD_A", "normal": 2, "golden": 1, "signature": 0, "diamond": 1, "trialNormal": 99 },
                { "cardId": "CARD_B", "normal": 1, "golden": 0 }
              ]
            }
            """);
        var source = CreateSource();
        source.UseExportFile(path);

        var snapshot = Assert.IsType<CollectionSnapshot>(await source.ReadAsync());

        Assert.Equal(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.FromHours(2)), snapshot.ObservedAt);
        Assert.Equal(2, snapshot.Cards.Count);
        Assert.Equal(new CollectionCard("CARD_A", 2, 1, 0, 1), snapshot.Cards[0]);
        Assert.Equal(new CollectionCard("CARD_B", 1, 0, null, null), snapshot.Cards[1]);
        Assert.Equal(ClientStateProviderStatus.Connected, source.Status);
    }

    [Fact]
    public async Task LocatorChoosesNewestFullExportAndExcludesDelta()
    {
        var documents = Path.Combine(_directory, "Documents");
        var exports = Path.Combine(documents, "HDT Collection Exports");
        Directory.CreateDirectory(exports);
        var older = Path.Combine(exports, "hearthstone-collection-20260910-120000.json");
        var newestDelta = Path.Combine(exports, "hearthstone-collection-changes-20260911-120000.json");
        var newestFull = Path.Combine(exports, "hearthstone-collection-20260911-110000.json");
        await File.WriteAllTextAsync(older, "{}");
        await File.WriteAllTextAsync(newestDelta, "{}");
        await File.WriteAllTextAsync(newestFull, "{}");
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newestDelta, new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newestFull, new DateTime(2026, 9, 11, 11, 0, 0, DateTimeKind.Utc));
        var locator = new HdtCollectionExportLocator(Path.Combine(_directory, "HDT"), documents);

        Assert.Equal(Path.GetFullPath(newestFull), locator.FindLatest());
        Assert.False(HdtCollectionExportLocator.IsFullExportFileName(Path.GetFileName(newestDelta)));
    }

    [Fact]
    public async Task AutoDiscoveryReadsSharedBaseline()
    {
        var hdt = Path.Combine(_directory, "HDT");
        var baseline = Path.Combine(hdt, "HdtCollectionExporter", HdtCollectionExportLocator.SharedBaselineFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(baseline)!);
        await File.WriteAllTextAsync(baseline, ValidExport("BASELINE"));
        var source = new HdtCollectionExportSource(
            new HdtCollectionExportLocator(hdt, Path.Combine(_directory, "Documents")));

        var snapshot = Assert.IsType<CollectionSnapshot>(await source.ReadAsync());

        Assert.Equal("BASELINE", Assert.Single(snapshot.Cards).CardId);
    }

    [Theory]
    [InlineData("{\"version\":2,\"exportedAt\":\"2026-09-11T12:00:00Z\",\"cards\":[]}")]
    [InlineData("{\"version\":3,\"version\":3,\"exportedAt\":\"2026-09-11T12:00:00Z\",\"cards\":[]}")]
    [InlineData("{\"version\":3,\"exportedAt\":\"not-a-date\",\"cards\":[]}")]
    [InlineData("{\"version\":3,\"exportedAt\":\"2026-09-11T12:00:00Z\",\"cards\":[{\"cardId\":\"A\",\"normal\":100,\"golden\":0}]}")]
    [InlineData("{\"version\":3,\"exportedAt\":\"2026-09-11T12:00:00Z\",\"cards\":[{\"cardId\":\"A\",\"normal\":1,\"golden\":0},{\"cardId\":\"A\",\"normal\":1,\"golden\":0}]}")]
    public async Task RejectsUnsupportedOrMalformedUntrustedDocuments(string json)
    {
        var path = await WriteExportAsync("hearthstone-collection-invalid.json", json);
        var source = CreateSource();
        source.UseExportFile(path);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await source.ReadAsync());
    }

    [Fact]
    public async Task RejectsFilesAboveTheInputBudgetBeforeJsonParsing()
    {
        var path = Path.Combine(_directory, "hearthstone-collection-too-large.json");
        Directory.CreateDirectory(_directory);
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(HdtCollectionExportSource.MaximumFileBytes + 1L);
        }

        var source = CreateSource();
        source.UseExportFile(path);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await source.ReadAsync());
    }

    [Fact]
    public async Task RejectsCardArraysAboveTheSnapshotBudget()
    {
        var json = JsonSerializer.Serialize(new
        {
            exportedAt = "2026-09-11T12:00:00Z",
            version = HdtCollectionExportSource.SupportedFormatVersion,
            cards = Enumerable.Range(0, CollectionSnapshot.MaximumCards + 1)
                .Select(index => new { cardId = $"CARD_{index}", normal = 1, golden = 0 }),
        });
        var path = await WriteExportAsync("hearthstone-collection-too-many.json", json);
        var source = CreateSource();
        source.UseExportFile(path);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await source.ReadAsync());
    }

    [Fact]
    public async Task MissingExportIsUnavailableWithoutCreatingDirectories()
    {
        var source = CreateSource();

        Assert.Equal(ClientStateProviderStatus.Unavailable, source.Status);
        Assert.Null(await source.ReadAsync());
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void ExplicitDeltaExportIsRejected()
    {
        var source = CreateSource();

        Assert.Throws<InvalidDataException>(() =>
            source.UseExportFile(Path.Combine(_directory, "hearthstone-collection-changes-20260911.json")));
    }

    private HdtCollectionExportSource CreateSource() => new(
        new HdtCollectionExportLocator(
            Path.Combine(_directory, "HDT"),
            Path.Combine(_directory, "Documents")));

    private async Task<string> WriteExportAsync(string fileName, string json)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
        return path;
    }

    private static string ValidExport(string cardId) => JsonSerializer.Serialize(new
    {
        exportedAt = "2026-09-11T12:00:00Z",
        version = HdtCollectionExportSource.SupportedFormatVersion,
        cards = new[] { new { cardId, normal = 1, golden = 0, signature = 0, diamond = 0 } },
    });
}
