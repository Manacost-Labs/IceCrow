using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using IceCrow.Hearthstone.Data;
using Xunit;
using Xunit.Abstractions;

namespace IceCrow.Infrastructure.ManacostApi.Tests;

public sealed class DataPerformanceBaselineTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "PerformanceDiagnostic")]
    public async Task LocalDataHotPathsProduceAnObservableBaseline()
    {
        using var temporary = new TemporaryDirectory();
        var cards = Enumerable.Range(1, 10_000)
            .Select(index => new CardDefinition(
                index,
                $"BG_TEST_{index}",
                $"Card {index}",
                null,
                "Text",
                "minion",
                null,
                (index % 7) + 1,
                [index % 2 == 0 ? "demon" : "beast"],
                true,
                false,
                null,
                CardImageInfo.Empty))
            .ToArray();
        var snapshot = HearthstoneDataSnapshotFactory.Create("benchmark", null, cards, []);
        var store = new JsonHearthstoneDataStore(Path.Combine(temporary.Path, "data.json"));

        var stopwatch = Stopwatch.StartNew();
        await store.SaveAsync(snapshot);
        var saveElapsed = stopwatch.Elapsed;
        stopwatch.Restart();
        var loaded = await store.LoadAsync();
        var loadElapsed = stopwatch.Elapsed;

        var database = new InMemoryCardDatabase();
        stopwatch.Restart();
        database.Replace(loaded!);
        var applyElapsed = stopwatch.Elapsed;
        stopwatch.Restart();
        for (var index = 0; index < 100_000; index++)
        {
            _ = database.GetByCardId($"BG_TEST_{(index % 10_000) + 1}");
            _ = database.GetByDbfId((index % 10_000) + 1);
        }

        var lookupElapsed = stopwatch.Elapsed;
        stopwatch.Restart();
        var filtered = database.QueryBattlegroundsCards(new CardQuery(6, "demon", true, false, "Card"));
        var filterElapsed = stopwatch.Elapsed;

        using var httpClient = new HttpClient(new StaticImageHandler());
        var imageCache = new CardImageDiskCache(
            Path.Combine(temporary.Path, "images"),
            httpClient,
            ["images.example.test"]);
        var imageUri = new Uri("https://images.example.test/card.png");
        _ = await imageCache.ResolveAsync(imageUri);
        stopwatch.Restart();
        for (var index = 0; index < 10_000; index++)
        {
            _ = await imageCache.GetCachedPathAsync(imageUri);
        }

        var imageLookupElapsed = stopwatch.Elapsed;
        Assert.NotEmpty(filtered);
        output.WriteLine(
            "10k records: save={0:F1}ms load={1:F1}ms apply={2:F1}ms; " +
            "100k CardId+DBF pairs={3:F1}ms; filter={4:F1}ms; 10k image-cache lookups={5:F1}ms",
            saveElapsed.TotalMilliseconds,
            loadElapsed.TotalMilliseconds,
            applyElapsed.TotalMilliseconds,
            lookupElapsed.TotalMilliseconds,
            filterElapsed.TotalMilliseconds,
            imageLookupElapsed.TotalMilliseconds);
    }

    private sealed class StaticImageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4]),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "IceCrowDataPerformance",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
