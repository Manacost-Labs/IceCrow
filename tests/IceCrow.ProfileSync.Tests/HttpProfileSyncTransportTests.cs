using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using IceCrow.ProfileSync.Records;
using IceCrow.ProfileSync.Transport;

namespace IceCrow.ProfileSync.Tests;

public sealed class HttpProfileSyncTransportTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri Origin = new("https://hearthpulse.test");

    [Fact]
    public async Task PostsTheBatchEnvelopeWithTheBearerTokenAndMapsAcknowledgements()
    {
        var events = new[] { Event(), Event() };
        var handler = new ScriptedHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("access-1", request.Headers.Authorization?.Parameter);
            Assert.Equal("https://hearthpulse.test/api/v1/tracker/events/batch", request.RequestUri?.ToString());
            var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
            var sent = body.RootElement.GetProperty("events");
            Assert.Equal(2, sent.GetArrayLength());
            Assert.Equal(events[0].EventId.ToString(), sent[0].GetProperty("eventId").GetString());
            Assert.Equal(1, sent[0].GetProperty("schemaVersion").GetInt32());
            return Json(HttpStatusCode.OK, $$"""{"accepted":["{{events[0].EventId}}"],"rejected":[{"eventId":"{{events[1].EventId}}","code":"invalid"}]}""");
        });
        var transport = Create(handler, Credential("access-1"));

        var result = await transport.UploadAsync(events, CancellationToken.None);

        Assert.Equal(ProfileUploadStatus.Accepted, result.Status);
        Assert.Equal([events[0].EventId], result.AcknowledgedEventIds);
        Assert.Equal([events[1].EventId], result.PermanentlyRejectedEventIds);
    }

    [Fact]
    public async Task RefreshesOnceAfterUnauthorizedAndRetriesWithTheNewToken()
    {
        var store = new MemoryCredentialStore(Credential("stale"));
        var calls = new List<string>();
        var handler = new ScriptedHandler(request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath + ":" + (request.Headers.Authorization?.Parameter ?? "form"));
            if (request.RequestUri.AbsolutePath == DeviceAuthorizationClient.TokenPath)
            {
                return Json(HttpStatusCode.OK, """{"access_token":"fresh","refresh_token":"refresh-2","expires_in":900,"scope":"profile.read tracker.write"}""");
            }

            return request.Headers.Authorization?.Parameter == "fresh"
                ? Json(HttpStatusCode.OK, """{"accepted":[],"rejected":[]}""")
                : Json(HttpStatusCode.Unauthorized, """{"error":"invalid_token"}""");
        });
        var transport = Create(handler, store);

        var result = await transport.UploadAsync([Event()], CancellationToken.None);

        Assert.Equal(ProfileUploadStatus.Accepted, result.Status);
        Assert.Equal(
            ["/api/v1/tracker/events/batch:stale", "/api/v1/oauth/token:form", "/api/v1/tracker/events/batch:fresh"],
            calls);
        Assert.Equal("fresh", store.Current?.AccessToken);
        Assert.Equal("refresh-2", store.Current?.RefreshToken);
    }

    [Fact]
    public async Task RevokedRefreshTokenClearsTheCredentialAndReportsUnauthorized()
    {
        var store = new MemoryCredentialStore(Credential("stale"));
        var handler = new ScriptedHandler(request =>
            request.RequestUri!.AbsolutePath == DeviceAuthorizationClient.TokenPath
                ? Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""")
                : Json(HttpStatusCode.Unauthorized, """{"error":"invalid_token"}"""));
        var transport = Create(handler, store);

        var result = await transport.UploadAsync([Event()], CancellationToken.None);

        Assert.Equal(ProfileUploadStatus.Unauthorized, result.Status);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task MissingCredentialNeverSendsAnything()
    {
        var handler = new ScriptedHandler(_ => throw new InvalidOperationException("must not be called"));
        var transport = Create(handler, new MemoryCredentialStore(null));

        var result = await transport.UploadAsync([Event()], CancellationToken.None);

        Assert.Equal(ProfileUploadStatus.Unauthorized, result.Status);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, ProfileUploadStatus.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, ProfileUploadStatus.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, ProfileUploadStatus.Unavailable)]
    public async Task ServerFailuresKeepTheBatchQueued(HttpStatusCode status, ProfileUploadStatus expected)
    {
        var handler = new ScriptedHandler(_ =>
        {
            var response = Json(status, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return response;
        });
        var transport = Create(handler, Credential("access-1"));

        var result = await transport.UploadAsync([Event()], CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Empty(result.AcknowledgedEventIds);
        Assert.Equal(TimeSpan.FromSeconds(90), result.RetryAfter);
    }

    [Fact]
    public async Task NetworkFailureIsUnavailableNotAnException()
    {
        var handler = new ScriptedHandler(_ => throw new HttpRequestException("offline"));
        var transport = Create(handler, Credential("access-1"));

        var result = await transport.UploadAsync([Event()], CancellationToken.None);

        Assert.Equal(ProfileUploadStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task MalformedBatchIsPermanentlyRejectedSoItCannotLoopForever()
    {
        var profileEvent = Event();
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.BadRequest, """{"error":"invalid_batch"}"""));
        var transport = Create(handler, Credential("access-1"));

        var result = await transport.UploadAsync([profileEvent], CancellationToken.None);

        Assert.Equal(ProfileUploadStatus.Accepted, result.Status);
        Assert.Equal([profileEvent.EventId], result.PermanentlyRejectedEventIds);
    }

    private static HttpProfileSyncTransport Create(ScriptedHandler handler, ProfileCredential credential) =>
        Create(handler, new MemoryCredentialStore(credential));

    private static HttpProfileSyncTransport Create(ScriptedHandler handler, MemoryCredentialStore store)
    {
        var client = new HttpClient(handler);
        return new HttpProfileSyncTransport(client, store, new DeviceAuthorizationClient(client, Origin));
    }

    private static ProfileCredential Credential(string accessToken) => new(
        Origin.ToString().TrimEnd('/'),
        accessToken,
        DateTimeOffset.UtcNow.AddMinutes(10),
        "refresh-1",
        ["profile.read", "tracker.write"],
        Timestamp);

    private static ProfileEvent Event() => ProfileEvent.Create(
        ProfileEventType.ArenaDraftPick,
        Timestamp,
        new ArenaDraftPickRecord(Guid.CreateVersion7(), 0, ["A", "B", "C"], "B", Timestamp, Certainty.Exact));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
    };

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class MemoryCredentialStore(ProfileCredential? initial) : IProfileCredentialStore
    {
        public ProfileCredential? Current { get; private set; } = initial;

        public Task<ProfileCredential?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

        public Task SaveAsync(ProfileCredential credential, CancellationToken cancellationToken = default)
        {
            Current = credential;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Current = null;
            return Task.CompletedTask;
        }
    }
}
