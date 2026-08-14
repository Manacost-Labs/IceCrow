using System.Text.Json;
using System.Text.Json.Serialization;

namespace IceCrow.Telemetry;

public sealed class TelemetryOutbox : IDisposable
{
    public const int MaximumItems = 128;
    private const int MaximumFileBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public TelemetryOutbox(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false)).Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> EnqueueAsync(
        MatchSummary summary,
        TelemetryConsent consent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(consent);
        if (!consent.IsEnabled)
        {
            return false;
        }

        Validate(summary);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!consent.IsEnabled)
            {
                return false;
            }

            var items = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            if (items.Any(item => item.MatchId == summary.MatchId))
            {
                return true;
            }

            if (items.Count >= MaximumItems)
            {
                items.RemoveAt(0);
            }

            items.Add(summary);
            await SaveUnsafeAsync(items, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MatchSummary>> PeekBatchAsync(
        int maximumItems,
        CancellationToken cancellationToken = default)
    {
        if (maximumItems is < 1 or > 25)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false))
                .Take(maximumItems)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AcknowledgeAsync(
        IEnumerable<Guid> matchIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matchIds);
        var acknowledged = matchIds.Take(25).ToHashSet();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            items.RemoveAll(item => acknowledged.Contains(item.MatchId));
            await SaveUnsafeAsync(items, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<MatchSummary>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var info = new FileInfo(_path);
        if (info.Length is <= 0 or > MaximumFileBytes)
        {
            throw new InvalidDataException("The telemetry outbox has an invalid size.");
        }

        try
        {
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var items = new List<MatchSummary>(MaximumItems);
            await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<MatchSummary>(
                               stream,
                               JsonOptions,
                               cancellationToken).ConfigureAwait(false))
            {
                if (item is null)
                {
                    throw new InvalidDataException("The telemetry outbox contains a null item.");
                }

                Validate(item);
                if (items.Count == MaximumItems)
                {
                    throw new InvalidDataException("The telemetry outbox item limit was exceeded.");
                }

                items.Add(item);
            }

            return items;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The telemetry outbox contains invalid JSON.", exception);
        }
    }

    private async Task SaveUnsafeAsync(List<MatchSummary> items, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The outbox path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, items, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (stream.Length > MaximumFileBytes)
                {
                    throw new InvalidDataException("The telemetry outbox exceeded its storage limit.");
                }
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

    private static void Validate(MatchSummary summary)
    {
        if (summary.GameMode is null ||
            summary.QueueMode is null ||
            summary.ClientVersion is null ||
            summary.TavernProgression is null ||
            summary.TavernProgression.Any(static entry => entry is null) ||
            summary.TelemetrySchemaVersion != MatchSummaryFactory.CurrentSchemaVersion ||
            summary.MatchId == Guid.Empty ||
            summary.EndedAt < summary.StartedAt ||
            summary.Turns is < 0 or > 100 ||
            summary.Triples is < 0 or > 100 ||
            summary.TavernProgression.Count > 16 ||
            summary.ClientVersion.Length > 64 ||
            summary.GameMode.Length > 32 ||
            summary.QueueMode.Length > 32 ||
            summary.HeroCardId?.Length > 128 ||
            summary.HearthstonePatch?.Length > 64 ||
            summary.RatingBucket?.Length > 32)
        {
            throw new InvalidDataException("The match summary is outside the telemetry contract limits.");
        }
    }
}
