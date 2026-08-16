using System.Buffers;
using System.Security.Cryptography;

namespace IceCrow.Hearthstone.Logs;

internal readonly record struct LogCheckpointContinuityResult(
    bool IsContinuous,
    LogResetReason? ResetReason)
{
    public static LogCheckpointContinuityResult Continuous { get; } = new(true, null);

    public static LogCheckpointContinuityResult Reset(LogResetReason reason) =>
        new(false, reason);
}

/// <summary>
/// Verifies that a reopened Power.log is still the append-only continuation of
/// a previously consumed checkpoint. All decisions use the open stream handle
/// (authoritative end-of-file), never directory metadata, and all reads are
/// bounded: at most the 4 KiB prefix plus the 64 KiB window that ends at the
/// consumed offset.
/// <para>
/// Append continuity is proven positively: the file is at least as long as the
/// consumed offset, the stored prefix fingerprint still matches, and the bytes
/// immediately before the consumed offset still hash to the checkpoint window
/// fingerprint. A growing file that satisfies these is never reset, regardless
/// of timestamps, creation-time changes, or stale directory metadata.
/// </para>
/// </summary>
internal static class LogCheckpointContinuity
{
    internal const int PrefixFingerprintBytes = 4 * 1024;
    internal const int ContinuityWindowBytes = 64 * 1024;
    internal const ulong InitialContentFingerprint = 14695981039346656037;
    private const ulong ContentFingerprintPrime = 1099511628211;
    private const int ReadBufferBytes = 16 * 1024;

    public static async Task<LogCheckpointContinuityResult> VerifyAsync(
        FileStream stream,
        string filePath,
        LogReadCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(checkpoint.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return LogCheckpointContinuityResult.Reset(LogResetReason.PathChanged);
        }

        if (stream.Length < checkpoint.ByteOffset)
        {
            return LogCheckpointContinuityResult.Reset(LogResetReason.FileShrank);
        }

        if (checkpoint.ByteOffset == 0)
        {
            return LogCheckpointContinuityResult.Continuous;
        }

        if (checkpoint.PrefixFingerprintLength > 0)
        {
            var prefixFingerprint = await ComputePrefixFingerprintAsync(
                stream,
                checkpoint.PrefixFingerprintLength,
                cancellationToken).ConfigureAwait(false);
            if (prefixFingerprint != checkpoint.PrefixFingerprint)
            {
                return LogCheckpointContinuityResult.Reset(LogResetReason.PrefixChangedBeforeRead);
            }
        }

        var windowFingerprint = await ComputeWindowFingerprintAsync(
            stream,
            checkpoint.ByteOffset,
            cancellationToken).ConfigureAwait(false);
        if (windowFingerprint != checkpoint.ContentFingerprint)
        {
            return LogCheckpointContinuityResult.Reset(LogResetReason.CheckpointWindowChanged);
        }

        return LogCheckpointContinuityResult.Continuous;
    }

    /// <summary>
    /// Rolling fingerprint of the up-to-64 KiB window that ends at
    /// <paramref name="endOffset"/>. The window is pinned to the offset, so the
    /// same offset always hashes the same bytes in an append-only file.
    /// </summary>
    public static async Task<ulong> ComputeWindowFingerprintAsync(
        FileStream stream,
        long endOffset,
        CancellationToken cancellationToken)
    {
        var fingerprint = InitialContentFingerprint;
        if (endOffset == 0)
        {
            return fingerprint;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
        try
        {
            var windowStart = Math.Max(0, endOffset - ContinuityWindowBytes);
            stream.Seek(windowStart, SeekOrigin.Begin);
            var remaining = endOffset - windowStart;
            while (remaining > 0)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(0, checked((int)Math.Min(buffer.Length, remaining))),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                for (var index = 0; index < read; index++)
                {
                    fingerprint = unchecked((fingerprint ^ buffer[index]) * ContentFingerprintPrime);
                }

                remaining -= read;
            }

            return fingerprint;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task<ulong> ComputePrefixFingerprintAsync(
        FileStream stream,
        int length,
        CancellationToken cancellationToken)
    {
        if (length == 0)
        {
            return 0;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            var total = 0;
            while (total < length)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(total, length - total),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            var hash = SHA256.HashData(buffer.AsSpan(0, total));
            return BitConverter.ToUInt64(hash);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
