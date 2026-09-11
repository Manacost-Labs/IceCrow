using System.Text.Json;

namespace IceCrow.ProfileSync.Outbox;

/// <summary>Bounded reader for the pre-journal JSON-array outbox format.</summary>
internal static class LegacyProfileOutboxFile
{
    public const int MaximumBytes = 16 * 1024 * 1024;

    public static async Task<LegacyProfileOutboxData?> ReadAsync(
        string path,
        int maximumHistoryItems,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaximumBytes)
        {
            throw new InvalidDataException("The legacy profile outbox has an invalid size.");
        }

        var history = new List<ProfileEvent>();
        ProfileEvent? collection = null;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<ProfileEvent>(
                               stream,
                               ProfileJson.Options,
                               cancellationToken).ConfigureAwait(false))
            {
                if (item is null)
                {
                    throw new InvalidDataException("The legacy profile outbox contains a null item.");
                }

                ProfileEvent.Validate(item);
                if (ProfileEventType.IsLatestOnly(item.Type))
                {
                    if (collection is not null)
                    {
                        throw new InvalidDataException("The legacy profile outbox contains multiple collection snapshots.");
                    }

                    collection = item;
                }
                else
                {
                    if (history.Count >= maximumHistoryItems)
                    {
                        throw new InvalidDataException("The legacy profile outbox event limit was exceeded.");
                    }

                    history.Add(item);
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The legacy profile outbox contains invalid JSON.", exception);
        }

        return new LegacyProfileOutboxData(history, collection);
    }

    public static void MarkMigrated(string path) => File.Move(path, path + ".migrated", overwrite: true);
}

internal sealed record LegacyProfileOutboxData(
    IReadOnlyList<ProfileEvent> History,
    ProfileEvent? Collection);
