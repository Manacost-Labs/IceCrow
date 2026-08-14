using System.Text.Json;
using System.Text.Json.Serialization;

namespace IceCrow.Telemetry;

public sealed record TelemetryPreferences(bool ShareAnonymousGameplayStatistics = false);

public sealed class TelemetryPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 4,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly string _path;

    public TelemetryPreferencesStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<TelemetryPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new TelemetryPreferences();
        }

        var info = new FileInfo(_path);
        if (info.Length is <= 0 or > 4096)
        {
            throw new InvalidDataException("The telemetry preferences file has an invalid size.");
        }

        try
        {
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<TelemetryPreferences>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false) ?? new TelemetryPreferences();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The telemetry preferences file contains invalid JSON.", exception);
        }
    }

    public async Task SaveAsync(TelemetryPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The preferences path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, preferences, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
