using System.Globalization;
using System.IO;

namespace IceCrow.Recording;

/// <summary>
/// Bounded local storage for private developer match captures. Captures are
/// written atomically (temporary file, flush, rename) with identity-free
/// names, never overwritten, and pruned oldest-first when the store exceeds
/// its limits. The store only ever touches capture and temporary files that
/// live directly inside its own root directory.
/// </summary>
public sealed class PrivateCaptureStore
{
    public const string CaptureFileExtension = ".icecrow.json";
    public const string TemporaryFilePrefix = ".icecrow-capture-";

    // Provisional bounds pending measurements of real match recordings; the
    // serializer already caps a single capture at MaximumFileBytes.
    public const int DefaultMaximumCaptureCount = 24;
    public const long DefaultMaximumTotalBytes = 256L * 1024 * 1024;

    private readonly int _maximumCaptureCount;
    private readonly long _maximumTotalBytes;

    public PrivateCaptureStore(
        string rootDirectory,
        int maximumCaptureCount = DefaultMaximumCaptureCount,
        long maximumTotalBytes = DefaultMaximumTotalBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCaptureCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumTotalBytes,
            RecordingSerializer.MaximumFileBytes);
        RootDirectory = Path.GetFullPath(rootDirectory);
        _maximumCaptureCount = maximumCaptureCount;
        _maximumTotalBytes = maximumTotalBytes;
    }

    public string RootDirectory { get; }

    /// <summary>
    /// Removes temporary files a crashed or cancelled save left behind. Call
    /// before the first save of a process, never while a save is running.
    /// </summary>
    public int CleanupAbandonedTemporaryFiles()
    {
        if (!Directory.Exists(RootDirectory))
        {
            return 0;
        }

        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(
                     RootDirectory,
                     $"{TemporaryFilePrefix}*.tmp",
                     SearchOption.TopDirectoryOnly))
        {
            if (IsDirectlyInsideRoot(path))
            {
                File.Delete(path);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Persists one recorded match as a new capture file and then enforces the
    /// retention bounds. Existing captures are never overwritten; a failed or
    /// cancelled save leaves no partially written capture behind.
    /// </summary>
    public async Task<PrivateCaptureSaveResult> SaveAsync(
        RecordedMatch match,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(match);
        RecordingSerializer.Validate(match);
        Directory.CreateDirectory(RootDirectory);

        var fileName = CreateCaptureFileName(match.StartedAt);
        var destination = Path.Combine(RootDirectory, fileName);
        if (File.Exists(destination))
        {
            throw new IOException($"Capture '{fileName}' already exists; refusing to overwrite.");
        }

        var temporaryPath = Path.Combine(
            RootDirectory,
            $"{TemporaryFilePrefix}{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await RecordingSerializer
                    .SerializeAsync(stream, match, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destination);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        // The capture is durable once the move above succeeded. Retention is
        // maintenance: a pruning failure degrades to a warning instead of
        // masquerading as loss of the freshly persisted capture.
        List<string> pruned;
        string? maintenanceWarning = null;
        try
        {
            pruned = EnforceRetention();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            pruned = [];
            maintenanceWarning =
                $"Retention pruning failed; older captures may remain: {exception.Message}";
        }

        return new PrivateCaptureSaveResult(destination, pruned, maintenanceWarning);
    }

    /// <summary>Lists stored captures ordered oldest first.</summary>
    public IReadOnlyList<PrivateCaptureInfo> ListCaptures()
    {
        if (!Directory.Exists(RootDirectory))
        {
            return [];
        }

        return EnumerateCaptureFiles()
            .Select(file => new PrivateCaptureInfo(
                file.FullName,
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)))
            .ToArray();
    }

    private List<string> EnforceRetention()
    {
        var captures = EnumerateCaptureFiles().ToList();
        var totalBytes = captures.Sum(static file => file.Length);
        var pruned = new List<string>();
        while (captures.Count > 0 &&
               (captures.Count > _maximumCaptureCount || totalBytes > _maximumTotalBytes))
        {
            var oldest = captures[0];
            captures.RemoveAt(0);
            totalBytes -= oldest.Length;
            File.Delete(oldest.FullName);
            pruned.Add(oldest.FullName);
        }

        return pruned;
    }

    private IEnumerable<FileInfo> EnumerateCaptureFiles()
    {
        // Capture names sort chronologically because they start with a UTC
        // timestamp, so ordinal filename order is oldest-first.
        return Directory
            .EnumerateFiles(
                RootDirectory,
                $"*{CaptureFileExtension}",
                SearchOption.TopDirectoryOnly)
            .Where(path =>
                IsDirectlyInsideRoot(path) &&
                !Path.GetFileName(path).StartsWith(TemporaryFilePrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(static path => new FileInfo(path));
    }

    private bool IsDirectlyInsideRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return string.Equals(
            Path.GetDirectoryName(fullPath),
            RootDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateCaptureFileName(DateTimeOffset startedAt) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{startedAt.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}_{Guid.NewGuid():N}{CaptureFileExtension}");
}

/// <summary>
/// Outcome of one capture save. The capture at <see cref="CapturePath"/> was
/// persisted successfully; <see cref="MaintenanceWarning"/> is set when
/// retention pruning afterwards failed without affecting the new capture.
/// </summary>
public sealed record PrivateCaptureSaveResult(
    string CapturePath,
    IReadOnlyList<string> PrunedCapturePaths,
    string? MaintenanceWarning = null)
{
    public bool RetentionSatisfied => MaintenanceWarning is null;
}

/// <summary>One stored capture file.</summary>
public sealed record PrivateCaptureInfo(
    string Path,
    long SizeBytes,
    DateTimeOffset LastWriteUtc);
