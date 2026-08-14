using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using IceCrow.Hearthstone.Data;
using Xunit;

namespace IceCrow.Infrastructure.ManacostApi.Tests;

public sealed class ManacostDataTests
{
    [Fact]
    public async Task PublicDatasetDownloadMapsCardsAndHeroesWithoutAuthorization()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            var json = request.RequestUri!.AbsolutePath.EndsWith("/cards", StringComparison.Ordinal)
                ? CardPage
                : HeroPage;
            return Json(HttpStatusCode.OK, json);
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = ManacostApiOptions.ProductionBaseAddress };
        var client = new ManacostDatasetClient(httpClient);

        var snapshot = await client.DownloadAsync();

        Assert.Single(snapshot.Cards);
        Assert.Equal("Гневопряд", snapshot.Cards[0].Name);
        Assert.Single(snapshot.Heroes);
        Assert.Equal("Рено Джексон", snapshot.Heroes[0].Name);
        Assert.Equal(64, snapshot.Version.Sha256.Length);
    }

    [Fact]
    public async Task OversizedAndMalformedResponsesAreRejected()
    {
        using var oversizedClient = new HttpClient(new StubHandler(_ =>
            Json(HttpStatusCode.OK, new string('x', 2048))))
        { BaseAddress = ManacostApiOptions.ProductionBaseAddress };
        var oversized = new ManacostDatasetClient(
            oversizedClient,
            new ManacostApiOptions { MaximumResponseBytes = 1024 });
        await Assert.ThrowsAsync<ManacostApiException>(() => oversized.DownloadAsync());

        using var malformedClient = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, "{")))
        { BaseAddress = ManacostApiOptions.ProductionBaseAddress };
        var malformed = new ManacostDatasetClient(malformedClient);
        await Assert.ThrowsAsync<ManacostApiException>(() => malformed.DownloadAsync());
    }

    [Fact]
    public async Task AggregateDatasetResponseBudgetSpansEveryPageAndResource()
    {
        using var httpClient = new HttpClient(new StubHandler(request =>
            Json(
                HttpStatusCode.OK,
                PadToLength(
                    request.RequestUri!.AbsolutePath.EndsWith("/cards", StringComparison.Ordinal)
                        ? CardPage
                        : HeroPage,
                    900))))
        { BaseAddress = ManacostApiOptions.ProductionBaseAddress };
        var client = new ManacostDatasetClient(
            httpClient,
            new ManacostApiOptions
            {
                MaximumResponseBytes = 1024,
                MaximumTotalResponseBytes = 1500,
            });

        var exception = await Assert.ThrowsAsync<ManacostApiException>(() => client.DownloadAsync());

        Assert.Contains("aggregate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CrossOriginFinalResponseIsRejected()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            var response = Json(HttpStatusCode.OK, CardPage);
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://localhost/internal");
            return response;
        }))
        { BaseAddress = ManacostApiOptions.ProductionBaseAddress };
        var client = new ManacostDatasetClient(httpClient);

        var exception = await Assert.ThrowsAsync<ManacostApiException>(() => client.DownloadAsync());

        Assert.Contains("origin", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedRefreshKeepsLastKnownGoodCache()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "cache.json");
        var store = new JsonHearthstoneDataStore(path);
        var expected = Snapshot("v1", "BG_OLD", 1);
        await store.SaveAsync(expected);
        var database = new InMemoryCardDatabase();
        using var httpClient = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.InternalServerError, "{}")))
        { BaseAddress = ManacostApiOptions.ProductionBaseAddress };
        var synchronizer = new ManacostDataSynchronizer(database, store, new ManacostDatasetClient(httpClient));

        await synchronizer.InitializeAsync();
        await synchronizer.RefreshAsync();

        Assert.Equal("BG_OLD", database.GetByDbfId(1)?.CardId);
        Assert.True(synchronizer.Status.OfflineMode);
        Assert.NotNull(synchronizer.Status.SyncError);
    }

    [Fact]
    public async Task InvalidReplacementCannotDestroyValidCache()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "cache.json");
        var store = new JsonHearthstoneDataStore(path);
        await store.SaveAsync(Snapshot("v1", "BG_OLD", 1));
        var invalid = new HearthstoneDataSnapshot(
            new HearthstoneDataVersion(1, "v2", null, new string('0', 64), DateTimeOffset.UtcNow),
            new[] { Card("BG_NEW", 2) },
            []);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(invalid));

        Assert.Equal("v1", (await store.LoadAsync())?.Version.DataVersion);
    }

    [Fact]
    public async Task TimeoutWithoutCacheLeavesTrackingDataEmptyAndOffline()
    {
        using var temporary = new TemporaryDirectory();
        var database = new InMemoryCardDatabase();
        using var httpClient = new HttpClient(new AsyncStubHandler(
            (_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout"))))
        { BaseAddress = ManacostApiOptions.ProductionBaseAddress };
        var synchronizer = new ManacostDataSynchronizer(
            database,
            new JsonHearthstoneDataStore(Path.Combine(temporary.Path, "cache.json")),
            new ManacostDatasetClient(httpClient));

        await synchronizer.InitializeAsync();
        await synchronizer.RefreshAsync();

        Assert.Equal(0, database.CardCount);
        Assert.False(synchronizer.Status.CacheReady);
        Assert.True(synchronizer.Status.OfflineMode);
    }

    [Fact]
    public async Task SameDatasetDoesNotRewriteCacheWhileNewDatasetDoes()
    {
        using var firstHttpClient = new HttpClient(new StubHandler(request =>
            Json(HttpStatusCode.OK, request.RequestUri!.AbsolutePath.EndsWith("/cards", StringComparison.Ordinal)
                ? CardPage
                : HeroPage)))
        { BaseAddress = ManacostApiOptions.ProductionBaseAddress };
        var existing = await new ManacostDatasetClient(firstHttpClient).DownloadAsync();
        var store = new CountingStore(existing);
        var database = new InMemoryCardDatabase();
        using var sameHttpClient = new HttpClient(new StubHandler(request =>
            Json(HttpStatusCode.OK, request.RequestUri!.AbsolutePath.EndsWith("/cards", StringComparison.Ordinal)
                ? CardPage
                : HeroPage)))
        { BaseAddress = ManacostApiOptions.ProductionBaseAddress };
        var synchronizer = new ManacostDataSynchronizer(
            database,
            store,
            new ManacostDatasetClient(sameHttpClient));

        await synchronizer.InitializeAsync();
        await synchronizer.RefreshAsync();

        Assert.Equal(0, store.SaveCount);

        using var changedHttpClient = new HttpClient(new StubHandler(request =>
            Json(HttpStatusCode.OK, request.RequestUri!.AbsolutePath.EndsWith("/cards", StringComparison.Ordinal)
                ? CardPage.Replace("BG21_001", "BG21_002", StringComparison.Ordinal)
                : HeroPage)))
        { BaseAddress = ManacostApiOptions.ProductionBaseAddress };
        var changedSynchronizer = new ManacostDataSynchronizer(
            database,
            store,
            new ManacostDatasetClient(changedHttpClient));

        await changedSynchronizer.RefreshAsync();

        Assert.Equal(1, store.SaveCount);
        Assert.NotNull(database.GetByCardId("BG21_002"));
    }

    [Fact]
    public async Task CorruptedHashAndUnsupportedSchemaAreRejected()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "cache.json");
        var store = new JsonHearthstoneDataStore(path);
        await store.SaveAsync(Snapshot("v1", "BG_OLD", 1));
        var validJson = await File.ReadAllTextAsync(path);

        await File.WriteAllTextAsync(path, validJson.Replace("BG_OLD", "BG_NEW", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());

        await File.WriteAllTextAsync(path, validJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
    }

    [Fact]
    public async Task NullRequiredCacheCollectionIsReportedAsInvalidData()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "cache.json");
        var store = new JsonHearthstoneDataStore(path);
        await store.SaveAsync(Snapshot("v1", "BG_OLD", 1));
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        document["cards"] = null;
        await File.WriteAllTextAsync(path, document.ToJsonString());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());

        Assert.Contains("null required values", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NullRequiredNestedCacheMemberIsReportedAsInvalidData()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "cache.json");
        var store = new JsonHearthstoneDataStore(path);
        await store.SaveAsync(Snapshot("v1", "BG_OLD", 1));
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        document["cards"]![0]!["creatureTypes"] = null;
        await File.WriteAllTextAsync(path, document.ToJsonString());

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
    }

    [Fact]
    public async Task ImageCacheDeduplicatesDownloadsAndRejectsUnapprovedHosts()
    {
        using var temporary = new TemporaryDirectory();
        var requests = 0;
        using var httpClient = new HttpClient(new AsyncStubHandler(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref requests);
            await Task.Delay(25, cancellationToken);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4]),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        }));
        var cache = new CardImageDiskCache(temporary.Path, httpClient, ["images.example.test"]);
        var imageUri = new Uri("https://images.example.test/card.png");

        var paths = await Task.WhenAll(cache.ResolveAsync(imageUri), cache.ResolveAsync(imageUri));

        Assert.Equal(1, requests);
        Assert.Equal(paths[0], paths[1]);
        Assert.True(File.Exists(paths[0]));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await cache.ResolveAsync(new Uri("https://localhost/private")));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static string PadToLength(string value, int length) =>
        value.Length >= length ? value : value + new string(' ', length - value.Length);

    private static HearthstoneDataSnapshot Snapshot(string version, string cardId, int dbf) =>
        HearthstoneDataSnapshotFactory.Create(version, null, new[] { Card(cardId, dbf) }, []);

    private static CardDefinition Card(string cardId, int dbf) => new(
        dbf, cardId, cardId, null, null, "minion", null, 1, Array.Empty<string>(), true, false, null, CardImageInfo.Empty);

    private const string CardPage = """
        {"data":[{"card_id":"BG21_001","dbf":72062,"name":{"ru":"Гневопряд","en":"Wrath Weaver"},"text_ru":"Текст","card_type":{"slug":"minion"},"tavern_tier":1,"creature_type":{"slug":"demon"},"in_pool":true,"duos_only":false,"images":{"card":"https://example.test/card.png"},"updated_at":"2026-08-14 10:00:00"}],"pagination":{"has_next":false}}
        """;
    private const string HeroPage = """
        {"data":[{"card_id":"TB_BaconShop_HERO_41","dbf":57946,"name":{"ru":"Рено Джексон","en":"Reno Jackson"},"health":30,"armor":{"normal":16,"duos":13},"images":{"hero":"https://example.test/hero.png"},"hero_power":{"dbf":58028,"card":{"name":"Gonna Be Rich!","text":"Make a minion Golden.","image":"https://example.test/power.png"}},"buddy":{"dbf":77777},"updated_at":"2026-08-14 11:00:00"}],"pagination":{"has_next":false}}
        """;

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = responder(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    private sealed class AsyncStubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request, cancellationToken);
    }

    private sealed class CountingStore(HearthstoneDataSnapshot initial) : IHearthstoneDataStore
    {
        private HearthstoneDataSnapshot _snapshot = initial;

        public int SaveCount { get; private set; }

        public Task<HearthstoneDataSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<HearthstoneDataSnapshot?>(_snapshot);
        }

        public Task SaveAsync(HearthstoneDataSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _snapshot = snapshot;
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IceCrowTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
