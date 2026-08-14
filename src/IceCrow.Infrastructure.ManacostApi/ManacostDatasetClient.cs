using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using IceCrow.Hearthstone.Data;

namespace IceCrow.Infrastructure.ManacostApi;

public sealed class ManacostDatasetClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32,
    };

    private readonly HttpClient _httpClient;
    private readonly ManacostApiOptions _options;

    public ManacostDatasetClient(HttpClient httpClient, ManacostApiOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _options = options ?? new ManacostApiOptions();
        _options.Validate();
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= _options.BaseAddress;
        if (_httpClient.BaseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("HttpClient.BaseAddress must use HTTPS.", nameof(httpClient));
        }

        _httpClient.Timeout = _options.Timeout;
    }

    public async Task<HearthstoneDataSnapshot> DownloadAsync(CancellationToken cancellationToken = default)
    {
        var budget = new DownloadBudget(_options.MaximumTotalResponseBytes);
        var cards = await DownloadPagesAsync<CardDto>("api/v1/cards", 20_000, budget, cancellationToken)
            .ConfigureAwait(false);
        var heroes = await DownloadPagesAsync<HeroDto>("api/v1/heroes", 1_000, budget, cancellationToken)
            .ConfigureAwait(false);
        var mappedCards = cards.Select(MapCard).ToArray();
        var mappedHeroes = heroes.Select(MapHero).ToArray();
        Validate(mappedCards, mappedHeroes);

        var updatedAt = cards.Select(card => card.UpdatedAt)
            .Concat(heroes.Select(hero => hero.UpdatedAt))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Order(StringComparer.Ordinal)
            .LastOrDefault();
        var dataVersion = BoundedOrNull(updatedAt, 64, "updated_at") ?? "unversioned";
        return HearthstoneDataSnapshotFactory.Create(dataVersion, null, mappedCards, mappedHeroes);
    }

    private async Task<List<T>> DownloadPagesAsync<T>(
        string path,
        int maximumItems,
        DownloadBudget budget,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();
        for (var page = 1; page <= _options.MaximumPages; page++)
        {
            var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            var uri = $"{path}{separator}page={page}&per_page={_options.PageSize}";
            var envelope = await GetJsonAsync<PageEnvelope<T>>(uri, budget, cancellationToken).ConfigureAwait(false);
            if (envelope.Data is null || envelope.Pagination is null)
            {
                throw new ManacostApiException("The Manacost API returned an incomplete page envelope.");
            }

            if (envelope.Data.Count > _options.PageSize || items.Count > maximumItems - envelope.Data.Count)
            {
                throw new ManacostApiException("The Manacost API dataset exceeded its record limit.");
            }

            items.AddRange(envelope.Data);
            if (!envelope.Pagination.HasNext)
            {
                return items;
            }
        }

        throw new ManacostApiException("The Manacost API page limit was exceeded.");
    }

    private async Task<T> GetJsonAsync<T>(
        string relativeUri,
        DownloadBudget budget,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            ValidateResponseOrigin(response);

            if (IsTransient(response.StatusCode) && attempt < 2)
            {
                var delay = RetryDelay(response, attempt);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ManacostApiException($"Manacost API returned HTTP {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength is long length && length > _options.MaximumResponseBytes)
            {
                throw new ManacostApiException("The Manacost API response exceeded the configured size limit.");
            }

            var bytes = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
            budget.Consume(bytes.Length);
            try
            {
                return JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                    ?? throw new ManacostApiException("The Manacost API returned an empty JSON value.");
            }
            catch (JsonException exception)
            {
                throw new ManacostApiException("The Manacost API returned malformed or incompatible JSON.", exception);
            }
        }

        throw new ManacostApiException("The Manacost API request failed after bounded retries.");
    }

    private async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream(Math.Min(_options.MaximumResponseBytes, 64 * 1024));
        var block = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(block, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > _options.MaximumResponseBytes)
            {
                throw new ManacostApiException("The Manacost API response exceeded the configured size limit.");
            }

            buffer.Write(block, 0, read);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout or
            HttpStatusCode.TooManyRequests;

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta <= TimeSpan.FromSeconds(10))
        {
            return delta;
        }

        return TimeSpan.FromMilliseconds((250 * (1 << attempt)) + Random.Shared.Next(25, 125));
    }

    private static CardDefinition MapCard(CardDto card)
    {
        if (card.Dbf is null or <= 0 || string.IsNullOrWhiteSpace(card.CardId) || card.Name is null)
        {
            throw new ManacostApiException("A card record is missing its stable identity.");
        }

        var creatureTypes = string.IsNullOrWhiteSpace(card.CreatureType?.Slug)
            ? Array.Empty<string>()
            : new[] { RequireBounded(card.CreatureType.Slug, 64, "creature_type.slug") };
        return new CardDefinition(
            card.Dbf.Value,
            RequireBounded(card.CardId, 128, "card_id"),
            RequireBounded(FirstAvailable(card.Name.Ru, card.Name.En, card.CardId), 512, "name"),
            BoundedOrNull(card.Name.En, 512, "name.en"),
            BoundedOrNull(card.TextRu, 4096, "text_ru"),
            RequireBounded(card.CardType?.Slug ?? "unknown", 64, "card_type.slug"),
            null,
            card.TavernTier,
            creatureTypes,
            card.InPool,
            card.DuosOnly,
            BoundedOrNull(card.GoldenVariant?.CardId, 128, "golden_variant.card_id"),
            MapImages(card.Images));
    }

    private static BattlegroundsHeroDefinition MapHero(HeroDto hero)
    {
        if (hero.Dbf is null or <= 0 || string.IsNullOrWhiteSpace(hero.CardId) || hero.Name is null)
        {
            throw new ManacostApiException("A hero record is missing its stable identity.");
        }

        HeroPowerDefinition? power = null;
        if (hero.HeroPower?.Dbf is not null || hero.HeroPower?.Card is not null)
        {
            power = new HeroPowerDefinition(
                hero.HeroPower?.Dbf,
                BoundedOrNull(hero.HeroPower?.Card?.Name, 512, "hero_power.name"),
                BoundedOrNull(hero.HeroPower?.Card?.Text, 4096, "hero_power.text"),
                MapHeroCardImages(hero.HeroPower?.Card));
        }

        return new BattlegroundsHeroDefinition(
            hero.Dbf.Value,
            RequireBounded(hero.CardId, 128, "hero.card_id"),
            RequireBounded(FirstAvailable(hero.Name.Ru, hero.Name.En, hero.CardId), 512, "hero.name"),
            BoundedOrNull(hero.Name.En, 512, "hero.name.en"),
            hero.Health,
            hero.Armor?.Normal,
            hero.Armor?.Duos,
            power,
            hero.Buddy?.Dbf,
            MapHeroImages(hero.Images));
    }

    private static CardImageInfo MapImages(ImagesDto? images) => new(
        HttpsUriOrNull(images?.Card),
        HttpsUriOrNull(images?.Golden),
        HttpsUriOrNull(images?.Art),
        HttpsUriOrNull(images?.Framed),
        HttpsUriOrNull(images?.Horizontal));

    private static CardImageInfo MapHeroImages(HeroImagesDto? images) => new(
        HttpsUriOrNull(images?.Hero), null, HttpsUriOrNull(images?.FullArt), null, HttpsUriOrNull(images?.Horizontal));

    private static CardImageInfo MapHeroCardImages(HeroCardDto? card) => new(
        HttpsUriOrNull(card?.Image), HttpsUriOrNull(card?.ImageGold), null, null, null);

    private static Uri? HttpsUriOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length > 2_048)
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return uri;
    }

    private void ValidateResponseOrigin(HttpResponseMessage response)
    {
        var finalUri = response.RequestMessage?.RequestUri;
        var expected = _httpClient.BaseAddress!;
        if (finalUri is null ||
            finalUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(finalUri.IdnHost, expected.IdnHost, StringComparison.OrdinalIgnoreCase) ||
            finalUri.Port != expected.Port)
        {
            throw new ManacostApiException("The Manacost API response escaped the configured HTTPS origin.");
        }
    }

    private static void Validate(CardDefinition[] cards, BattlegroundsHeroDefinition[] heroes)
    {
        if (cards.Length > 20_000 || heroes.Length > 1_000)
        {
            throw new ManacostApiException("The dataset exceeded the supported record count.");
        }

        if (cards.Select(card => card.CardId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != cards.Length ||
            cards.Select(card => card.DbfId).Distinct().Count() != cards.Length)
        {
            throw new ManacostApiException("The dataset contains duplicate card identities.");
        }

        if (cards.Any(card => card.TavernTier is < 1 or > 7))
        {
            throw new ManacostApiException("The dataset contains an invalid Battlegrounds tavern tier.");
        }
    }

    private static string FirstAvailable(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!;

    private static string RequireBounded(string value, int maximumLength, string field) =>
        BoundedOrNull(value, maximumLength, field)
        ?? throw new ManacostApiException($"The {field} field is required.");

    private static string? BoundedOrNull(string? value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length > maximumLength)
        {
            throw new ManacostApiException($"The {field} field exceeded its size limit.");
        }

        return value;
    }

    private sealed record PageEnvelope<T>(
        [property: JsonPropertyName("data")] List<T>? Data,
        [property: JsonPropertyName("pagination")] PaginationDto? Pagination);

    private sealed record PaginationDto(
        [property: JsonPropertyName("has_next")] bool HasNext);

    private sealed record LocalizedNameDto(
        [property: JsonPropertyName("ru")] string? Ru,
        [property: JsonPropertyName("en")] string? En);

    private sealed record SlugDto([property: JsonPropertyName("slug")] string? Slug);

    private sealed record ImagesDto(
        [property: JsonPropertyName("card")] string? Card,
        [property: JsonPropertyName("golden")] string? Golden,
        [property: JsonPropertyName("art")] string? Art,
        [property: JsonPropertyName("framed")] string? Framed,
        [property: JsonPropertyName("horizontal")] string? Horizontal);

    private sealed record GoldenVariantDto([property: JsonPropertyName("card_id")] string? CardId);

    private sealed record CardDto(
        [property: JsonPropertyName("card_id")] string CardId,
        [property: JsonPropertyName("dbf")] int? Dbf,
        [property: JsonPropertyName("name")] LocalizedNameDto? Name,
        [property: JsonPropertyName("text_ru")] string? TextRu,
        [property: JsonPropertyName("card_type")] SlugDto? CardType,
        [property: JsonPropertyName("tavern_tier")] int? TavernTier,
        [property: JsonPropertyName("creature_type")] SlugDto? CreatureType,
        [property: JsonPropertyName("in_pool")] bool InPool,
        [property: JsonPropertyName("duos_only")] bool DuosOnly,
        [property: JsonPropertyName("images")] ImagesDto? Images,
        [property: JsonPropertyName("golden_variant")] GoldenVariantDto? GoldenVariant,
        [property: JsonPropertyName("updated_at")] string? UpdatedAt);

    private sealed record HeroImagesDto(
        [property: JsonPropertyName("hero")] string? Hero,
        [property: JsonPropertyName("full_art")] string? FullArt,
        [property: JsonPropertyName("horizontal")] string? Horizontal);

    private sealed record ArmorDto(
        [property: JsonPropertyName("normal")] int? Normal,
        [property: JsonPropertyName("duos")] int? Duos);

    private sealed record HeroCardDto(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("image")] string? Image,
        [property: JsonPropertyName("image_gold")] string? ImageGold);

    private sealed record HeroRelationDto(
        [property: JsonPropertyName("dbf")] int? Dbf,
        [property: JsonPropertyName("card")] HeroCardDto? Card);

    private sealed record HeroDto(
        [property: JsonPropertyName("card_id")] string CardId,
        [property: JsonPropertyName("dbf")] int? Dbf,
        [property: JsonPropertyName("name")] LocalizedNameDto? Name,
        [property: JsonPropertyName("health")] int? Health,
        [property: JsonPropertyName("armor")] ArmorDto? Armor,
        [property: JsonPropertyName("images")] HeroImagesDto? Images,
        [property: JsonPropertyName("hero_power")] HeroRelationDto? HeroPower,
        [property: JsonPropertyName("buddy")] HeroRelationDto? Buddy,
        [property: JsonPropertyName("updated_at")] string? UpdatedAt);

    private sealed class DownloadBudget(int maximumBytes)
    {
        private int _remainingBytes = maximumBytes;

        public void Consume(int bytes)
        {
            if (bytes < 0 || bytes > _remainingBytes)
            {
                throw new ManacostApiException("The Manacost API aggregate response budget was exceeded.");
            }

            _remainingBytes -= bytes;
        }
    }
}
