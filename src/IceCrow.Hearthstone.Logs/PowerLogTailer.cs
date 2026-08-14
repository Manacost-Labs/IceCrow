using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace IceCrow.Hearthstone.Logs;

/// <summary>
/// Tails the active Power.log into a bounded channel. The channel uses
/// <see cref="BoundedChannelFullMode.Wait"/> so a slow consumer pauses file progress
/// instead of growing memory without limit or dropping accepted log lines.
/// </summary>
public sealed class PowerLogTailer
{
    private const int DefaultChannelCapacity = 128;
    private const int MaximumChannelCapacity = 256;
    private const int ReadBufferBytes = 16 * 1024;
    private const int MaximumLineBytes = 64 * 1024;
    private const int FingerprintBytes = 4 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] AcceptedPrefixes =
    [
        "PowerTaskList.DebugPrintPower",
        "GameState.",
        "PowerProcessor.EndCurrentTaskList",
    ];

    private readonly HearthstoneLogLocator _locator;
    private readonly Channel<RawLogLine> _lines;
    private readonly Channel<bool> _signals;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _recoveryInterval;
    private readonly Dictionary<string, FileSystemWatcher> _watchers =
        new(StringComparer.OrdinalIgnoreCase);
    private LogReadCheckpoint _checkpoint = LogReadCheckpoint.Empty;
    private bool _discardingOversizedLine;
    private int _started;

    public PowerLogTailer(
        HearthstoneLogLocator locator,
        int channelCapacity = DefaultChannelCapacity,
        TimeSpan? recoveryInterval = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCapacity);
        if (channelCapacity > MaximumChannelCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelCapacity),
                $"The channel capacity cannot exceed {MaximumChannelCapacity} lines.");
        }

        _locator = locator;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _recoveryInterval = recoveryInterval ?? TimeSpan.FromSeconds(1);
        if (_recoveryInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recoveryInterval),
                "The recovery interval must be positive.");
        }

        _lines = Channel.CreateBounded<RawLogLine>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
        _signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public event Action<Exception>? RecoverableError;

    public ChannelReader<RawLogLine> Lines => _lines.Reader;

    public LogReadCheckpoint Checkpoint => _checkpoint;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("A PowerLogTailer instance can only be started once.");
        }

        Exception? completionError = null;
        try
        {
            Signal();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RefreshWatchers();
                await ReadAvailableAsync(cancellationToken).ConfigureAwait(false);
                await WaitForSignalOrRecoveryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            completionError = exception;
            throw;
        }
        finally
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
            _signals.Writer.TryComplete();
            _lines.Writer.TryComplete(completionError);
        }
    }

    private void RefreshWatchers()
    {
        foreach (var root in _locator.GetLogRoots())
        {
            if (_watchers.ContainsKey(root) || !Directory.Exists(root))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.DirectoryName |
                                   NotifyFilters.FileName |
                                   NotifyFilters.LastWrite |
                                   NotifyFilters.Size,
                    Filter = "*",
                };
                watcher.Changed += OnFileSystemChanged;
                watcher.Created += OnFileSystemChanged;
                watcher.Deleted += OnFileSystemChanged;
                watcher.Renamed += OnFileSystemChanged;
                watcher.Error += OnWatcherError;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(root, watcher);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                RecoverableError?.Invoke(exception);
            }
        }
    }

    private async Task ReadAvailableAsync(CancellationToken cancellationToken)
    {
        string? path;
        try
        {
            path = _locator.FindPowerLog();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            RecoverableError?.Invoke(exception);
            return;
        }

        if (path is null)
        {
            return;
        }

        try
        {
            await ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            RecoverableError?.Invoke(exception);
        }
    }

    private async Task ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists)
        {
            return;
        }

        var createdAt = new DateTimeOffset(file.CreationTimeUtc, TimeSpan.Zero);
        var lastWriteAt = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
        await using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: ReadBufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var samePath = string.Equals(
            _checkpoint.FilePath,
            file.FullName,
            StringComparison.OrdinalIgnoreCase);
        var prefixChanged = samePath &&
                            _checkpoint.PrefixFingerprintLength > 0 &&
                            (stream.Length < _checkpoint.PrefixFingerprintLength ||
                             await ComputePrefixFingerprintAsync(
                                 stream,
                                 _checkpoint.PrefixFingerprintLength,
                                 cancellationToken).ConfigureAwait(false) != _checkpoint.PrefixFingerprint);
        var samePathRewrite =
            samePath &&
            _checkpoint.FileCreatedAt == createdAt &&
            file.Length <= _checkpoint.ByteOffset &&
            file.Length <= _checkpoint.ObservedLength &&
            lastWriteAt > _checkpoint.LastWriteAt;
        if (!samePath ||
            _checkpoint.FileCreatedAt != createdAt ||
            file.Length < _checkpoint.ByteOffset ||
            prefixChanged ||
            samePathRewrite)
        {
            _checkpoint = new LogReadCheckpoint(
                file.FullName,
                0,
                createdAt,
                file.Length,
                lastWriteAt,
                0,
                0);
            _discardingOversizedLine = false;
        }

        stream.Seek(_checkpoint.ByteOffset, SeekOrigin.Begin);

        var rentedBuffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
        using var lineBuffer = new MemoryStream(capacity: 4096);

        try
        {
            while (true)
            {
                var bytesRead = await stream.ReadAsync(
                    rentedBuffer.AsMemory(0, ReadBufferBytes),
                    cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                var absoluteOffset = stream.Position - bytesRead;
                for (var index = 0; index < bytesRead; index++)
                {
                    absoluteOffset++;
                    var current = rentedBuffer[index];

                    if (_discardingOversizedLine)
                    {
                        _checkpoint = _checkpoint with { ByteOffset = absoluteOffset };
                        if (current == (byte)'\n')
                        {
                            _discardingOversizedLine = false;
                        }

                        continue;
                    }

                    if (current == (byte)'\n')
                    {
                        await ProcessCompleteLineAsync(lineBuffer, cancellationToken).ConfigureAwait(false);
                        lineBuffer.SetLength(0);
                        _checkpoint = _checkpoint with { ByteOffset = absoluteOffset };
                        continue;
                    }

                    if (lineBuffer.Length >= MaximumLineBytes)
                    {
                        RecoverableError?.Invoke(new InvalidDataException(
                            $"A Power.log line exceeded the {MaximumLineBytes}-byte safety limit and was skipped."));
                        lineBuffer.SetLength(0);
                        _discardingOversizedLine = true;
                        _checkpoint = _checkpoint with { ByteOffset = absoluteOffset };
                        continue;
                    }

                    lineBuffer.WriteByte(current);
                }
            }

            file.Refresh();
            var fingerprintLength = checked((int)Math.Min(
                FingerprintBytes,
                Math.Min(_checkpoint.ByteOffset, stream.Length)));
            var fingerprint = await ComputePrefixFingerprintAsync(
                stream,
                fingerprintLength,
                cancellationToken).ConfigureAwait(false);
            _checkpoint = _checkpoint with
            {
                ObservedLength = file.Exists ? file.Length : _checkpoint.ObservedLength,
                LastWriteAt = file.Exists
                    ? new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)
                    : _checkpoint.LastWriteAt,
                PrefixFingerprint = fingerprint,
                PrefixFingerprintLength = fingerprintLength,
            };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    private static async Task<ulong> ComputePrefixFingerprintAsync(
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

    private async Task ProcessCompleteLineAsync(
        MemoryStream lineBuffer,
        CancellationToken cancellationToken)
    {
        var length = checked((int)lineBuffer.Length);
        if (length > 0 && lineBuffer.GetBuffer()[length - 1] == (byte)'\r')
        {
            length--;
        }

        if (length == 0)
        {
            return;
        }

        string original;
        try
        {
            original = StrictUtf8.GetString(lineBuffer.GetBuffer(), 0, length);
        }
        catch (DecoderFallbackException exception)
        {
            RecoverableError?.Invoke(exception);
            return;
        }

        if (!TryParseAcceptedLine(original, out var rawLine))
        {
            return;
        }

        await _lines.Writer.WriteAsync(rawLine, cancellationToken).ConfigureAwait(false);
    }

    private bool TryParseAcceptedLine(string original, out RawLogLine line)
    {
        line = null!;
        if (original.Length < 4 || original[1] != ' ' || original[0] is not ('D' or 'W' or 'E'))
        {
            return false;
        }

        var timestampEnd = original.IndexOf(' ', 2);
        if (timestampEnd < 0 || timestampEnd == original.Length - 1)
        {
            return false;
        }

        var timestampText = original[2..timestampEnd];
        var content = original[(timestampEnd + 1)..];
        if (!AcceptedPrefixes.Any(prefix => content.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return false;
        }

        var now = _timeProvider.GetLocalNow();
        var timestamp = now;
        if (TimeSpan.TryParse(timestampText, CultureInfo.InvariantCulture, out var timeOfDay) &&
            timeOfDay >= TimeSpan.Zero &&
            timeOfDay < TimeSpan.FromDays(1))
        {
            var localDateTime = now.Date + timeOfDay;
            timestamp = new DateTimeOffset(
                localDateTime,
                TimeZoneInfo.Local.GetUtcOffset(localDateTime));
            if (timestamp > now.AddHours(1))
            {
                timestamp = timestamp.AddDays(-1);
            }
        }

        line = new RawLogLine(timestamp, "Power", content, original);
        return true;
    }

    private async Task WaitForSignalOrRecoveryAsync(CancellationToken cancellationToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var signalTask = _signals.Reader.ReadAsync(waitCancellation.Token).AsTask();
        var recoveryTask = Task.Delay(_recoveryInterval, _timeProvider, waitCancellation.Token);
        var completed = await Task.WhenAny(signalTask, recoveryTask).ConfigureAwait(false);
        waitCancellation.Cancel();

        try
        {
            await completed.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        while (_signals.Reader.TryRead(out _))
        {
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        Signal();
    }

    private void OnWatcherError(object sender, ErrorEventArgs eventArgs)
    {
        _ = sender;
        RecoverableError?.Invoke(eventArgs.GetException());
        Signal();
    }

    private void Signal()
    {
        _signals.Writer.TryWrite(true);
    }
}
