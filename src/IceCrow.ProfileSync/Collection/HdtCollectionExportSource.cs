using System.Globalization;
using System.Text.Json;
using IceCrow.Hearthstone.ClientState;

namespace IceCrow.ProfileSync.Collection;

/// <summary>
/// Clean-room file bridge for complete schema-v3 JSON snapshots produced by
/// the Manacost HDT Collection Exporter. Only owned card identifiers and
/// finish counts are read; account identity, dust and gameplay statistics are
/// deliberately ignored.
/// </summary>
public sealed class HdtCollectionExportSource : ICollectionSource
{
    public const int SupportedFormatVersion = 3;
    public const int MaximumFileBytes = 16 * 1024 * 1024;
    private const int MaximumJsonDepth = 16;

    private readonly HdtCollectionExportLocator _locator;
    private string? _explicitPath;

    public HdtCollectionExportSource(HdtCollectionExportLocator locator)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
    }

    public ClientStateProviderStatus Status => ResolvePath() is null
        ? ClientStateProviderStatus.Unavailable
        : ClientStateProviderStatus.Connected;

    /// <summary>
    /// Selects one complete export for the next and subsequent reads. The path
    /// is resolved immediately; file contents remain untrusted and are checked
    /// by <see cref="ReadAsync"/>.
    /// </summary>
    public void UseExportFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!HdtCollectionExportLocator.IsFullExportFileName(Path.GetFileName(fullPath)))
        {
            throw new InvalidDataException("The selected file is not a complete collection export.");
        }

        Volatile.Write(ref _explicitPath, fullPath);
    }

    public async ValueTask<CollectionSnapshot?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var path = ResolvePath();
        if (path is null)
        {
            return null;
        }

        var bytes = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
        try
        {
            ReadOnlyMemory<byte> json = bytes;
            if (bytes.AsSpan().StartsWith("\uFEFF"u8))
            {
                json = bytes.AsMemory(3);
            }

            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = MaximumJsonDepth,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowDuplicateProperties = false,
            });
            return Parse(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The collection export contains invalid JSON.", exception);
        }
    }

    private string? ResolvePath() => Volatile.Read(ref _explicitPath) ?? _locator.FindLatest();

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaximumFileBytes)
        {
            throw new InvalidDataException($"A collection export must be between 1 and {MaximumFileBytes} bytes.");
        }

        var bytes = new byte[checked((int)stream.Length)];
        try
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The collection export changed while it was being read.", exception);
        }

        var extra = new byte[1];
        if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new InvalidDataException("The collection export changed while it was being read.");
        }

        return bytes;
    }

    private static CollectionSnapshot Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The collection export root must be an object.");
        }

        var version = ReadRequiredInt(root, "version");
        if (version != SupportedFormatVersion)
        {
            throw new InvalidDataException($"Collection export version {version} is not supported.");
        }

        if (!root.TryGetProperty("exportedAt", out var exportedAtElement) ||
            exportedAtElement.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(
                exportedAtElement.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var exportedAt))
        {
            throw new InvalidDataException("The collection export has an invalid exportedAt timestamp.");
        }

        if (!root.TryGetProperty("cards", out var cardsElement) || cardsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The collection export has no cards array.");
        }

        if (cardsElement.GetArrayLength() > CollectionSnapshot.MaximumCards)
        {
            throw new InvalidDataException($"The collection export exceeds {CollectionSnapshot.MaximumCards} cards.");
        }

        var cards = new List<CollectionCard>(cardsElement.GetArrayLength());
        var cardIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in cardsElement.EnumerateArray())
        {
            var card = ParseCard(element);
            if (!cardIds.Add(card.CardId))
            {
                throw new InvalidDataException("The collection export contains duplicate card identifiers.");
            }

            cards.Add(card);
        }

        return new CollectionSnapshot(exportedAt, cards);
    }

    private static CollectionCard ParseCard(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("cardId", out var cardIdElement) ||
            cardIdElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("A collection card has no valid cardId.");
        }

        try
        {
            return new CollectionCard(
                cardIdElement.GetString()!,
                ReadRequiredInt(element, "normal"),
                ReadRequiredInt(element, "golden"),
                ReadOptionalInt(element, "signature"),
                ReadOptionalInt(element, "diamond"));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("A collection card contains an invalid identifier or count.", exception);
        }
    }

    private static int ReadRequiredInt(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var value))
        {
            throw new InvalidDataException($"The collection export has no valid {propertyName} value.");
        }

        return value;
    }

    private static int? ReadOptionalInt(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw new InvalidDataException($"The collection export has an invalid {propertyName} value.");
        }

        return value;
    }
}
