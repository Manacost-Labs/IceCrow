using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IceCrow.ProfileSync.Transport;

/// <summary>
/// Uploads event batches to <c>POST {origin}/api/v1/tracker/events/batch</c>
/// with the device bearer token, refreshing it once on 401. Expected HTTP and
/// network failures become <see cref="ProfileUploadResult"/> statuses; only
/// cancellation and programming faults propagate.
/// </summary>
public sealed class HttpProfileSyncTransport : IProfileSyncTransport
{
    public const string BatchPath = "/api/v1/tracker/events/batch";
    public const int MaximumResponseBytes = BoundedResponse.MaximumBytes;

    // Server responses may grow new members; never let an unknown field turn
    // an accepted batch into an endless retry.
    private static readonly JsonSerializerOptions ResponseOptions = new(ProfileJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    private readonly HttpClient _httpClient;
    private readonly IProfileCredentialStore _credentials;
    private readonly DeviceAuthorizationClient _authorization;
    private readonly TimeProvider _time;

    public HttpProfileSyncTransport(
        HttpClient httpClient,
        IProfileCredentialStore credentials,
        DeviceAuthorizationClient authorization,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(authorization);
        _httpClient = httpClient;
        _credentials = credentials;
        _authorization = authorization;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<ProfileUploadResult> UploadAsync(
        IReadOnlyList<ProfileEvent> events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count is 0 or > ProfileOutbox.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(events));
        }

        var credential = await LoadUsableCredentialAsync(cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return ProfileUploadResult.Unauthorized();
        }

        var result = await SendAsync(credential, events, cancellationToken).ConfigureAwait(false);
        if (result.Status != ProfileUploadStatus.Unauthorized)
        {
            return result;
        }

        var refreshed = await _authorization.RefreshAsync(credential, cancellationToken).ConfigureAwait(false);
        if (refreshed is null)
        {
            await _credentials.ClearAsync(cancellationToken).ConfigureAwait(false);
            return ProfileUploadResult.Unauthorized();
        }

        await _credentials.SaveAsync(refreshed, cancellationToken).ConfigureAwait(false);
        return await SendAsync(refreshed, events, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProfileCredential?> LoadUsableCredentialAsync(CancellationToken cancellationToken)
    {
        var credential = await _credentials.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (credential is null || !credential.IsAccessTokenExpired(_time.GetUtcNow()))
        {
            return credential;
        }

        var refreshed = await _authorization.RefreshAsync(credential, cancellationToken).ConfigureAwait(false);
        if (refreshed is null)
        {
            await _credentials.ClearAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        await _credentials.SaveAsync(refreshed, cancellationToken).ConfigureAwait(false);
        return refreshed;
    }

    private async Task<ProfileUploadResult> SendAsync(
        ProfileCredential credential,
        IReadOnlyList<ProfileEvent> events,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(credential.ServerOrigin), BatchPath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Content = JsonContent.Create(new BatchEnvelope(events), options: ProfileJson.Options);
        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return await TranslateAsync(response, events, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return ProfileUploadResult.Unavailable();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout, not a caller cancellation.
            return ProfileUploadResult.Unavailable();
        }
    }

    private static async Task<ProfileUploadResult> TranslateAsync(
        HttpResponseMessage response,
        IReadOnlyList<ProfileEvent> events,
        CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
            case HttpStatusCode.Accepted:
                return await ReadAcceptedAsync(response, events, cancellationToken).ConfigureAwait(false);
            case HttpStatusCode.Unauthorized:
                return ProfileUploadResult.Unauthorized();
            case HttpStatusCode.TooManyRequests:
                return ProfileUploadResult.RateLimited(ReadRetryAfter(response));
            case HttpStatusCode.RequestEntityTooLarge:
            case HttpStatusCode.BadRequest:
            case HttpStatusCode.UnprocessableEntity:
                // The whole batch is malformed from the server's point of view;
                // the events will never be accepted as sent.
                return new ProfileUploadResult(
                    ProfileUploadStatus.Accepted,
                    [],
                    events.Select(static item => item.EventId).ToArray());
            default:
                return ProfileUploadResult.Unavailable(ReadRetryAfter(response));
        }
    }

    private static async Task<ProfileUploadResult> ReadAcceptedAsync(
        HttpResponseMessage response,
        IReadOnlyList<ProfileEvent> events,
        CancellationToken cancellationToken)
    {
        var body = await BoundedResponse.ReadAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return ProfileUploadResult.Unavailable();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<BatchResponse>(body, ResponseOptions);
            if (parsed is null)
            {
                return ProfileUploadResult.Unavailable();
            }

            var submitted = events.Select(static item => item.EventId).ToHashSet();
            var rejected = (parsed.Rejected ?? [])
                .Where(item => item.EventId.HasValue && submitted.Contains(item.EventId.Value))
                .Select(static item => item.EventId!.Value)
                .ToArray();
            return new ProfileUploadResult(
                ProfileUploadStatus.Accepted,
                (parsed.Accepted ?? []).Where(submitted.Contains).ToArray(),
                rejected);
        }
        catch (JsonException)
        {
            return ProfileUploadResult.Unavailable();
        }
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta <= TimeSpan.FromHours(1) ? delta : TimeSpan.FromHours(1);
        }

        return retryAfter?.Date is { } date && date > DateTimeOffset.UtcNow
            ? date - DateTimeOffset.UtcNow
            : null;
    }

    private sealed record BatchEnvelope(IReadOnlyList<ProfileEvent> Events);

    private sealed record BatchResponse(IReadOnlyList<Guid>? Accepted, IReadOnlyList<RejectedEvent>? Rejected);

    private sealed record RejectedEvent(Guid? EventId, string? Code)
    {
        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{EventId}:{Code}");
    }
}
