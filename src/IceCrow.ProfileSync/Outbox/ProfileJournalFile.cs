using System.Text;
using System.Text.Json;

namespace IceCrow.ProfileSync.Outbox;

/// <summary>
/// Append-only JSON Lines journal of history events and acknowledgement
/// tombstones. One enqueue is one appended line flushed to disk; nothing is
/// rewritten per gameplay event. Compaction rewrites the file atomically
/// (temp file + move) only when tombstones accumulate.
/// </summary>
internal sealed class ProfileJournalFile
{
    public const int MaximumBytes = 64 * 1024 * 1024;
    private const string EventProperty = "event";
    private const string AckProperty = "ack";
    private static readonly byte[] NewLine = "\n"u8.ToArray();

    private readonly string _path;

    public ProfileJournalFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = System.IO.Path.GetFullPath(path);
    }

    public string Path => _path;

    public long Length => File.Exists(_path) ? new FileInfo(_path).Length : 0;

    /// <summary>
    /// Replays the journal. A crash can leave an incomplete final line; that
    /// tail is dropped and counted rather than rejecting the whole history.
    /// Any other malformed line is corruption and throws.
    /// </summary>
    public async Task<JournalReplay> ReplayAsync(
        int maximumLiveEvents,
        int maximumMaterializedEvents,
        CancellationToken cancellationToken)
    {
        if (maximumLiveEvents < 1 || maximumMaterializedEvents < maximumLiveEvents)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLiveEvents));
        }

        if (!File.Exists(_path))
        {
            return new JournalReplay([], 0, 0, false);
        }

        var fileLength = new FileInfo(_path).Length;
        if (fileLength > MaximumBytes)
        {
            throw new InvalidDataException("The profile journal exceeds its size limit.");
        }

        if (fileLength == 0)
        {
            return new JournalReplay([], 0, 0, false);
        }

        var events = new List<ProfileEvent>();
        var index = new Dictionary<Guid, int>();
        var tombstones = 0;
        var truncatedTail = false;
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var endsWithNewLine = EndsWithNewLine(stream);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        string? line;
        var lineNumber = 0;
        try
        {
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                lineNumber++;
                if (line.Length == 0)
                {
                    continue;
                }

                if (!TryParseLine(line, out var profileEvent, out var ack))
                {
                    if (IsLastLine(reader) && !endsWithNewLine)
                    {
                        truncatedTail = true;
                        break;
                    }

                    throw new InvalidDataException($"The profile journal line {lineNumber} is corrupt.");
                }

                if (profileEvent is not null)
                {
                    if (!index.ContainsKey(profileEvent.EventId))
                    {
                        if (index.Count >= maximumLiveEvents || events.Count >= maximumMaterializedEvents)
                        {
                            throw new InvalidDataException("The profile journal event limit was exceeded.");
                        }

                        index[profileEvent.EventId] = events.Count;
                        events.Add(profileEvent);
                    }
                }
                else if (ack is { } acknowledged)
                {
                    if (!index.ContainsKey(acknowledged))
                    {
                        throw new InvalidDataException($"The profile journal line {lineNumber} acknowledges an unknown event.");
                    }

                    tombstones++;
                    if (index.Remove(acknowledged, out var position))
                    {
                        events[position] = null!;
                    }
                }
            }
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The profile journal contains invalid UTF-8.", exception);
        }

        events.RemoveAll(static item => item is null);
        return new JournalReplay(events, tombstones, lineNumber, truncatedTail);
    }

    public async Task<bool> TryAppendEventAsync(ProfileEvent profileEvent, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.SerializeToUtf8Bytes(new JournalLine(profileEvent, null), ProfileJson.Options);
        return await TryAppendAsync(line, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryAppendAcksAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken)
    {
        if (eventIds.Count == 0)
        {
            return true;
        }

        using var buffer = new MemoryStream();
        foreach (var id in eventIds)
        {
            buffer.Write(JsonSerializer.SerializeToUtf8Bytes(new JournalLine(null, id), ProfileJson.Options));
            buffer.Write(NewLine);
        }

        return await TryAppendRawAsync(buffer.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rewrites the journal with only the live events, atomically.</summary>
    public async Task CompactAsync(IReadOnlyList<ProfileEvent> live, CancellationToken cancellationToken)
    {
        var directory = EnsureDirectory();
        var temporaryPath = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                foreach (var profileEvent in live)
                {
                    await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(new JournalLine(profileEvent, null), ProfileJson.Options), cancellationToken).ConfigureAwait(false);
                    await stream.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (stream.Length > MaximumBytes)
                {
                    throw new InvalidDataException("The compacted profile journal exceeds its size limit.");
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

    private async Task<bool> TryAppendAsync(byte[] line, CancellationToken cancellationToken)
    {
        var payload = new byte[line.Length + NewLine.Length];
        line.CopyTo(payload, 0);
        NewLine.CopyTo(payload, line.Length);
        return await TryAppendRawAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryAppendRawAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        EnsureDirectory();
        await using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        if (bytes.Length > MaximumBytes || stream.Length > MaximumBytes - bytes.Length)
        {
            return false;
        }

        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private string EnsureDirectory()
    {
        var directory = System.IO.Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The journal path has no parent directory.");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static bool IsLastLine(StreamReader reader) => reader.EndOfStream;

    private static bool EndsWithNewLine(FileStream stream)
    {
        stream.Seek(-1, SeekOrigin.End);
        var result = stream.ReadByte() == '\n';
        stream.Seek(0, SeekOrigin.Begin);
        return result;
    }

    private static bool TryParseLine(string line, out ProfileEvent? profileEvent, out Guid? ack)
    {
        profileEvent = null;
        ack = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<JournalLine>(line, ProfileJson.Options);
            if (parsed is null || (parsed.Event is null) == (parsed.Ack is null))
            {
                return false;
            }

            if (parsed.Event is { } item)
            {
                ProfileEvent.Validate(item);
                if (ProfileEventType.IsLatestOnly(item.Type))
                {
                    return false;
                }

                profileEvent = item;
            }
            else
            {
                ack = parsed.Ack;
            }

            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return false;
        }
    }

    private sealed record JournalLine(ProfileEvent? Event, Guid? Ack)
    {
        public override string ToString() => Event is null ? $"{AckProperty}:{Ack}" : $"{EventProperty}:{Event.EventId}";
    }
}

internal sealed record JournalReplay(
    List<ProfileEvent> Events,
    int Tombstones,
    int Lines,
    bool TruncatedTailRecovered);
