using System.Net;
using IceCrow.ProfileSync.Transport;

namespace IceCrow.ProfileSync.Tests;

public sealed class DeviceAuthorizationClientTests
{
    private static readonly Uri Origin = new("https://hearthpulse.test");

    [Fact]
    public async Task StartRequestsTheLeastPrivilegeScopesAndReturnsTheUserCode()
    {
        string? form = null;
        var client = Create(request =>
        {
            form = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, """{"device_code":"dev-1","user_code":"ABCD-EFGH","verification_uri":"https://hearthpulse.test/connect/","verification_uri_complete":"https://hearthpulse.test/connect/?user_code=ABCD-EFGH","expires_in":600,"interval":5}""");
        });

        var start = await client.StartAsync(CancellationToken.None);

        Assert.NotNull(start);
        Assert.Equal("ABCD-EFGH", start.UserCode);
        Assert.Equal("dev-1", start.DeviceCode);
        Assert.Equal(TimeSpan.FromSeconds(5), start.Interval);
        Assert.Contains("client_id=manacost-tracker", form, StringComparison.Ordinal);
        Assert.Contains("scope=profile.read+tracker.write", form, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"error":"authorization_pending"}""", DeviceLinkOutcome.Pending, 5)]
    [InlineData("""{"error":"slow_down"}""", DeviceLinkOutcome.Pending, 10)]
    [InlineData("""{"error":"expired_token"}""", DeviceLinkOutcome.Expired, 5)]
    [InlineData("""{"error":"access_denied"}""", DeviceLinkOutcome.Denied, 5)]
    public async Task PollMapsOAuthErrorsWithoutThrowing(string body, DeviceLinkOutcome expected, int nextIntervalSeconds)
    {
        var client = Create(_ => Json(HttpStatusCode.BadRequest, body));
        var start = new DeviceLinkStart("dev-1", "CODE", "https://hearthpulse.test/connect/", null, DateTimeOffset.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(5));

        var poll = await client.PollAsync(start, CancellationToken.None);

        Assert.Equal(expected, poll.Outcome);
        Assert.Null(poll.Credential);
        Assert.Equal(TimeSpan.FromSeconds(nextIntervalSeconds), poll.NextInterval);
    }

    [Fact]
    public async Task PollReturnsARevocableCredentialWithScopesAndOrigin()
    {
        var client = Create(_ => Json(HttpStatusCode.OK, """{"access_token":"mca_access_x","refresh_token":"mca_refresh_y","token_type":"Bearer","expires_in":900,"scope":"profile.read tracker.write"}"""));
        var start = new DeviceLinkStart("dev-1", "CODE", "https://hearthpulse.test/connect/", null, DateTimeOffset.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(5));

        var poll = await client.PollAsync(start, CancellationToken.None);

        Assert.Equal(DeviceLinkOutcome.Linked, poll.Outcome);
        Assert.NotNull(poll.Credential);
        Assert.Equal("mca_access_x", poll.Credential.AccessToken);
        Assert.Equal("mca_refresh_y", poll.Credential.RefreshToken);
        Assert.Equal("https://hearthpulse.test", poll.Credential.ServerOrigin);
        Assert.Equal(["profile.read", "tracker.write"], poll.Credential.Scopes);
        Assert.False(poll.Credential.IsAccessTokenExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task TransportFailureKeepsTheExistingCredentialOnRefresh()
    {
        var client = Create(_ => throw new HttpRequestException("offline"));
        var credential = new ProfileCredential("https://hearthpulse.test", "a", DateTimeOffset.UtcNow, "r", [], DateTimeOffset.UtcNow);

        var refreshed = await client.RefreshAsync(credential, CancellationToken.None);

        Assert.Same(credential, refreshed);
    }

    [Fact]
    public void RejectsInsecureOrigins()
    {
        using var httpClient = new HttpClient();

        Assert.Throws<ArgumentException>(() => new DeviceAuthorizationClient(httpClient, new Uri("http://hearthpulse.test")));
    }

    private static DeviceAuthorizationClient Create(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new ScriptedHandler(respond)), Origin);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
    };

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
