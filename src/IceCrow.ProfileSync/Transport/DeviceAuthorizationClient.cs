using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IceCrow.ProfileSync.Transport;

/// <summary>
/// OAuth 2.0 device authorization (RFC 8628) against HearthPulse
/// <c>/api/v1/oauth/device/code</c> and <c>/api/v1/oauth/token</c>. The user
/// links the device by entering the short code on the HearthPulse connect
/// page; IceCrow then holds only a revocable, least-privilege device
/// credential. No shared or admin token exists anywhere in the client.
/// </summary>
public sealed class DeviceAuthorizationClient
{
    public const string DefaultClientId = "manacost-tracker";
    public const string DeviceCodePath = "/api/v1/oauth/device/code";
    public const string TokenPath = "/api/v1/oauth/token";
    public const string RevokePath = "/api/v1/oauth/revoke";
    public static readonly IReadOnlyList<string> RequiredScopes = ["profile.read", "tracker.write"];

    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        MaxDepth = 8,
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _origin;
    private readonly string _clientId;
    private readonly TimeProvider _time;

    public DeviceAuthorizationClient(
        HttpClient httpClient,
        Uri serverOrigin,
        string clientId = DefaultClientId,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(serverOrigin);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        if (!serverOrigin.IsAbsoluteUri || serverOrigin.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The HearthPulse origin must be an absolute https URL.", nameof(serverOrigin));
        }

        _httpClient = httpClient;
        _origin = serverOrigin;
        _clientId = clientId;
        _time = timeProvider ?? TimeProvider.System;
    }

    public Uri ServerOrigin => _origin;

    /// <summary>Starts linking; returns null when the server is unavailable.</summary>
    public async Task<DeviceLinkStart?> StartAsync(CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["scope"] = string.Join(' ', RequiredScopes),
        };
        var response = await PostFormAsync<DeviceCodeResponse>(DeviceCodePath, form, cancellationToken).ConfigureAwait(false);
        if (response.Body is null ||
            string.IsNullOrEmpty(response.Body.DeviceCode) ||
            string.IsNullOrEmpty(response.Body.UserCode) ||
            string.IsNullOrEmpty(response.Body.VerificationUri))
        {
            return null;
        }

        var now = _time.GetUtcNow();
        return new DeviceLinkStart(
            response.Body.DeviceCode,
            response.Body.UserCode,
            response.Body.VerificationUri,
            response.Body.VerificationUriComplete,
            now.AddSeconds(Math.Clamp(response.Body.ExpiresIn, 30, 3600)),
            TimeSpan.FromSeconds(Math.Clamp(response.Body.Interval, 5, 60)));
    }

    /// <summary>One poll of the token endpoint for a pending device code.</summary>
    public async Task<DeviceLinkPoll> PollAsync(DeviceLinkStart start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["device_code"] = start.DeviceCode,
            ["client_id"] = _clientId,
        };
        var response = await PostFormAsync<TokenResponse>(TokenPath, form, cancellationToken).ConfigureAwait(false);
        if (response.Body?.AccessToken is { Length: > 0 } && response.Body.RefreshToken is { Length: > 0 })
        {
            return new DeviceLinkPoll(DeviceLinkOutcome.Linked, ToCredential(response.Body), start.Interval);
        }

        return response.Body?.Error switch
        {
            "authorization_pending" => new DeviceLinkPoll(DeviceLinkOutcome.Pending, null, start.Interval),
            "slow_down" => new DeviceLinkPoll(DeviceLinkOutcome.Pending, null, start.Interval + TimeSpan.FromSeconds(5)),
            "expired_token" => new DeviceLinkPoll(DeviceLinkOutcome.Expired, null, start.Interval),
            "access_denied" => new DeviceLinkPoll(DeviceLinkOutcome.Denied, null, start.Interval),
            _ when response.StatusCode is HttpStatusCode.TooManyRequests =>
                new DeviceLinkPoll(DeviceLinkOutcome.Pending, null, start.Interval + TimeSpan.FromSeconds(5)),
            _ => new DeviceLinkPoll(DeviceLinkOutcome.Unavailable, null, start.Interval),
        };
    }

    /// <summary>Rotates the refresh token; null means the credential is no longer valid.</summary>
    public async Task<ProfileCredential?> RefreshAsync(ProfileCredential credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = credential.RefreshToken,
            ["client_id"] = _clientId,
        };
        var response = await PostFormAsync<TokenResponse>(TokenPath, form, cancellationToken).ConfigureAwait(false);
        if (response.Body?.AccessToken is { Length: > 0 } && response.Body.RefreshToken is { Length: > 0 })
        {
            return ToCredential(response.Body) with { LinkedAt = credential.LinkedAt };
        }

        // A transport failure keeps the old credential; only an explicit
        // rejection (400/401 with an OAuth error) invalidates it.
        return response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
            ? null
            : credential;
    }

    public async Task<bool> RevokeAsync(ProfileCredential credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        var form = new Dictionary<string, string>
        {
            ["token"] = credential.RefreshToken,
            ["token_type_hint"] = "refresh_token",
            ["client_id"] = _clientId,
        };
        var response = await PostFormAsync<TokenResponse>(RevokePath, form, cancellationToken).ConfigureAwait(false);
        return response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;
    }

    private ProfileCredential ToCredential(TokenResponse token)
    {
        var scopes = (token.Scope ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(ProfileCredential.MaximumScopes)
            .ToArray();
        var now = _time.GetUtcNow();
        return new ProfileCredential(
            _origin.GetLeftPart(UriPartial.Authority),
            token.AccessToken!,
            now.AddSeconds(Math.Clamp(token.ExpiresIn ?? 900, 60, 86_400)),
            token.RefreshToken!,
            scopes,
            now);
    }

    private async Task<(HttpStatusCode StatusCode, TBody? Body)> PostFormAsync<TBody>(
        string path,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
        where TBody : class
    {
        using var content = new FormUrlEncodedContent(form);
        try
        {
            using var response = await _httpClient
                .PostAsync(new Uri(_origin, path), content, cancellationToken)
                .ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is > HttpProfileSyncTransport.MaximumResponseBytes)
            {
                return (response.StatusCode, null);
            }

            var body = await response.Content.ReadFromJsonAsync<TBody>(WireOptions, cancellationToken).ConfigureAwait(false);
            return (response.StatusCode, body);
        }
        catch (HttpRequestException)
        {
            return (HttpStatusCode.ServiceUnavailable, null);
        }
        catch (JsonException)
        {
            return (HttpStatusCode.ServiceUnavailable, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (HttpStatusCode.ServiceUnavailable, null);
        }
    }

    private sealed record DeviceCodeResponse(
        string? DeviceCode,
        string? UserCode,
        string? VerificationUri,
        string? VerificationUriComplete,
        int ExpiresIn,
        int Interval);

    private sealed record TokenResponse(
        string? AccessToken,
        string? RefreshToken,
        string? TokenType,
        int? ExpiresIn,
        string? Scope,
        string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
}

public enum DeviceLinkOutcome
{
    Pending,
    Linked,
    Expired,
    Denied,
    Unavailable,
}

/// <summary>What the user must do: open the verification page and enter the code.</summary>
public sealed record DeviceLinkStart(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string? VerificationUriComplete,
    DateTimeOffset ExpiresAt,
    TimeSpan Interval);

public sealed record DeviceLinkPoll(DeviceLinkOutcome Outcome, ProfileCredential? Credential, TimeSpan NextInterval);
