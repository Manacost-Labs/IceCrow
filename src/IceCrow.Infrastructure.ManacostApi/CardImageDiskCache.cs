using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace IceCrow.Infrastructure.ManacostApi;

public sealed class CardImageDiskCache : ICardImageResolver
{
    private const int MaximumImageBytes = 8 * 1024 * 1024;
    private readonly string _directory;
    private readonly HttpClient _httpClient;
    private readonly long _maximumCacheBytes;
    private readonly HashSet<string> _allowedHosts;
    private readonly ConcurrentDictionary<Uri, Lazy<Task<string?>>> _downloads = new();
    private readonly ConcurrentDictionary<Uri, DateTimeOffset> _negativeCache = new();

    public CardImageDiskCache(
        string directory,
        HttpClient httpClient,
        IEnumerable<string> allowedHosts,
        long maximumCacheBytes = 256L * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(allowedHosts);
        if (maximumCacheBytes is < MaximumImageBytes or > 4L * 1024 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCacheBytes));
        }

        _directory = Path.GetFullPath(directory);
        _httpClient = httpClient;
        _maximumCacheBytes = maximumCacheBytes;
        _allowedHosts = allowedHosts
            .Select(host => host.Trim().TrimEnd('.'))
            .Where(host => host.Length > 0 && !host.Contains('*', StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_allowedHosts.Count == 0)
        {
            throw new ArgumentException("At least one explicit image host is required.", nameof(allowedHosts));
        }
    }

    public ValueTask<string?> GetCachedPathAsync(Uri imageUri, CancellationToken cancellationToken = default)
    {
        ValidateUri(imageUri);
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(imageUri);
        return ValueTask.FromResult<string?>(File.Exists(path) ? path : null);
    }

    public async Task<string?> ResolveAsync(Uri imageUri, CancellationToken cancellationToken = default)
    {
        ValidateUri(imageUri);
        var cached = await GetCachedPathAsync(imageUri, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        if (_negativeCache.TryGetValue(imageUri, out var retryAfter) && retryAfter > DateTimeOffset.UtcNow)
        {
            return null;
        }

        var lazy = _downloads.GetOrAdd(
            imageUri,
            uri => new Lazy<Task<string?>>(
                () => DownloadAndRemoveAsync(uri),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> DownloadAndRemoveAsync(Uri imageUri)
    {
        try
        {
            return await DownloadAsync(imageUri, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _downloads.TryRemove(imageUri, out _);
        }
    }

    private async Task<string?> DownloadAsync(Uri imageUri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                imageUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                RememberFailure(imageUri);
                return null;
            }

            if (response.Content.Headers.ContentLength is long length && length > MaximumImageBytes)
            {
                RememberFailure(imageUri);
                return null;
            }

            if (response.Content.Headers.ContentType?.MediaType is not string mediaType ||
                !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                RememberFailure(imageUri);
                return null;
            }

            Directory.CreateDirectory(_directory);
            var path = GetPath(imageUri);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    var block = new byte[16 * 1024];
                    var total = 0;
                    while (true)
                    {
                        var read = await input.ReadAsync(block, cancellationToken).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        total = checked(total + read);
                        if (total > MaximumImageBytes)
                        {
                            return null;
                        }

                        await output.WriteAsync(block.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }

                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporaryPath, path, overwrite: true);
                TrimIfNeeded();
                return path;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            RememberFailure(imageUri);
            return null;
        }
    }

    private void RememberFailure(Uri imageUri)
    {
        if (_negativeCache.Count >= 1024)
        {
            _negativeCache.Clear();
        }

        _negativeCache[imageUri] = DateTimeOffset.UtcNow.AddMinutes(5);
    }

    private void TrimIfNeeded()
    {
        var files = new DirectoryInfo(_directory)
            .EnumerateFiles("*.img", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToArray();
        var total = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (total <= _maximumCacheBytes)
            {
                break;
            }

            total -= file.Length;
            file.Delete();
        }
    }

    private string GetPath(Uri uri)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        return Path.Combine(_directory, hash + ".img");
    }

    private void ValidateUri(Uri imageUri)
    {
        ArgumentNullException.ThrowIfNull(imageUri);
        if (!imageUri.IsAbsoluteUri ||
            imageUri.Scheme != Uri.UriSchemeHttps ||
            !_allowedHosts.Contains(imageUri.IdnHost.TrimEnd('.')))
        {
            throw new ArgumentException("Card image URLs must use HTTPS and an explicitly allowed host.", nameof(imageUri));
        }
    }
}
