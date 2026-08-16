using System.Text;

namespace IceCrow.Hearthstone.Logs.Tests;

/// <summary>
/// Unit-level regression tests for the append-only continuity invariant. The
/// real-client false reread was caused by decisions that mixed directory
/// metadata (FileInfo) with handle state and compared content fingerprints of
/// two different windows; these tests pin the reopened-file decision to the
/// authoritative handle and to a window anchored at the consumed offset.
/// </summary>
public sealed class LogCheckpointContinuityTests : IDisposable
{
    private static readonly Encoding LogEncoding = new UTF8Encoding(false);
    private readonly TemporaryLogDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task AppendOnlyGrowthIsContinuous()
    {
        var path = _directory.GetPath("Power.log");
        await File.WriteAllTextAsync(path, Lines(0, 100), LogEncoding);
        var checkpoint = await CheckpointAtEndOfFileAsync(path);

        await File.AppendAllTextAsync(path, Lines(100, 50), LogEncoding);

        var result = await VerifyAsync(path, checkpoint);
        Assert.True(result.IsContinuous);
    }

    [Fact]
    public async Task StaleObservedLengthBelowOffsetIsStillContinuous()
    {
        // Real-client race state: the checkpoint advanced via the read handle
        // while the last observed FileInfo length lagged behind. Directory
        // metadata must play no part in the reopened-file decision.
        var path = _directory.GetPath("Power.log");
        await File.WriteAllTextAsync(path, Lines(0, 100), LogEncoding);
        var checkpoint = await CheckpointAtEndOfFileAsync(path);
        checkpoint = checkpoint with { ObservedLength = checkpoint.ByteOffset - 10 };

        await File.AppendAllTextAsync(path, Lines(100, 5), LogEncoding);

        var result = await VerifyAsync(path, checkpoint);
        Assert.True(result.IsContinuous);
    }

    [Fact]
    public async Task ArmedSameLengthObservationWithGrowthIsContinuous()
    {
        // Real-client race state: ObservedLength == ByteOffset (caught-up idle
        // file) while Hearthstone appended between the metadata snapshot and
        // the read. The old algorithm armed a same-length rewrite check and
        // then compared fingerprints of two different windows, resetting to
        // byte zero mid-match.
        var path = _directory.GetPath("Power.log");
        await File.WriteAllTextAsync(path, Lines(0, 100), LogEncoding);
        var checkpoint = await CheckpointAtEndOfFileAsync(path);
        Assert.Equal(checkpoint.ByteOffset, checkpoint.ObservedLength);

        await File.AppendAllTextAsync(path, Lines(100, 20), LogEncoding);

        var result = await VerifyAsync(path, checkpoint);
        Assert.True(result.IsContinuous);
    }

    [Fact]
    public async Task CreationTimeMismatchAloneIsContinuous()
    {
        var path = _directory.GetPath("Power.log");
        await File.WriteAllTextAsync(path, Lines(0, 100), LogEncoding);
        var checkpoint = await CheckpointAtEndOfFileAsync(path);
        checkpoint = checkpoint with
        {
            FileCreatedAt = checkpoint.FileCreatedAt.AddMinutes(-5),
        };

        await File.AppendAllTextAsync(path, Lines(100, 5), LogEncoding);

        var result = await VerifyAsync(path, checkpoint);
        Assert.True(result.IsContinuous);
    }

    [Fact]
    public async Task TruncationBelowTheConsumedOffsetResets()
    {
        var path = _directory.GetPath("Power.log");
        await File.WriteAllTextAsync(path, Lines(0, 100), LogEncoding);
        var checkpoint = await CheckpointAtEndOfFileAsync(path);

        await File.WriteAllTextAsync(path, Lines(0, 10), LogEncoding);

        var result = await VerifyAsync(path, checkpoint);
        Assert.False(result.IsContinuous);
        Assert.Equal(LogResetReason.FileShrank, result.ResetReason);
    }

    [Fact]
    public async Task SameLengthRewriteInsideTheCheckpointWindowResets()
    {
        var path = _directory.GetPath("Power.log");
        await File.WriteAllTextAsync(path, Lines(0, 100), LogEncoding);
        var checkpoint = await CheckpointAtEndOfFileAsync(path);

        await OverwriteByteAsync(path, checkpoint.ByteOffset - 4, (byte)'X');

        var result = await VerifyAsync(path, checkpoint);
        Assert.False(result.IsContinuous);
        Assert.Equal(LogResetReason.CheckpointWindowChanged, result.ResetReason);
    }

    [Fact]
    public async Task PrefixRewriteOutsideTheCheckpointWindowResets()
    {
        // File long enough that the 4 KiB prefix and the 64 KiB tail window do
        // not overlap; a rewrite of an early byte must still reset.
        var path = _directory.GetPath("Power.log");
        await File.WriteAllTextAsync(path, Lines(0, 1200), LogEncoding);
        var checkpoint = await CheckpointAtEndOfFileAsync(path);
        Assert.True(checkpoint.ByteOffset > 64 * 1024 + 4 * 1024);

        await OverwriteByteAsync(path, 100, (byte)'X');

        var result = await VerifyAsync(path, checkpoint);
        Assert.False(result.IsContinuous);
        Assert.Equal(LogResetReason.PrefixChangedBeforeRead, result.ResetReason);
    }

    [Fact]
    public async Task DifferentPathResets()
    {
        var path = _directory.GetPath("Power.log");
        await File.WriteAllTextAsync(path, Lines(0, 10), LogEncoding);
        var checkpoint = await CheckpointAtEndOfFileAsync(path);
        checkpoint = checkpoint with { FilePath = _directory.GetPath("Other.log") };

        var result = await VerifyAsync(path, checkpoint);
        Assert.False(result.IsContinuous);
        Assert.Equal(LogResetReason.PathChanged, result.ResetReason);
    }

    private static async Task<LogCheckpointContinuityResult> VerifyAsync(
        string path,
        LogReadCheckpoint checkpoint)
    {
        await using var stream = OpenShared(path);
        return await LogCheckpointContinuity.VerifyAsync(
            stream,
            new FileInfo(path).FullName,
            checkpoint,
            CancellationToken.None);
    }

    private static async Task<LogReadCheckpoint> CheckpointAtEndOfFileAsync(string path)
    {
        await using var stream = OpenShared(path);
        var length = stream.Length;
        var prefixLength = checked((int)Math.Min(
            LogCheckpointContinuity.PrefixFingerprintBytes,
            length));
        var prefix = await LogCheckpointContinuity.ComputePrefixFingerprintAsync(
            stream,
            prefixLength,
            CancellationToken.None);
        var window = await LogCheckpointContinuity.ComputeWindowFingerprintAsync(
            stream,
            length,
            CancellationToken.None);
        var file = new FileInfo(path);
        return new LogReadCheckpoint(
            file.FullName,
            length,
            new DateTimeOffset(file.CreationTimeUtc, TimeSpan.Zero),
            length,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            prefix,
            prefixLength,
            window);
    }

    private static async Task OverwriteByteAsync(string path, long offset, byte value)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        stream.Seek(offset, SeekOrigin.Begin);
        await stream.WriteAsync(new[] { value }, CancellationToken.None);
        await stream.FlushAsync(CancellationToken.None);
    }

    private static FileStream OpenShared(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 4096,
        FileOptions.Asynchronous);

    private static string Lines(int start, int count) => string.Concat(
        Enumerable.Range(start, count).Select(index =>
            $"D 17:00:00.0000000 GameState.DebugPrintGame() - line-{index:D5}\r\n"));
}
