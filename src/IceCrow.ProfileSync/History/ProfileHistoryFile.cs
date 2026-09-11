using System.Text;
using System.Text.Json;

namespace IceCrow.ProfileSync.History;

/// <summary>Strict bounded JSON Lines filesystem boundary for local history.</summary>
internal sealed class ProfileHistoryFile
{
    private const int MaximumLineCharacters = ProfileEvent.MaximumPayloadBytes + 16 * 1024;
    private static readonly byte[] NewLine = "\n"u8.ToArray();
    private readonly string _path;

    public ProfileHistoryFile(string path) => _path = Path.GetFullPath(path);

    public async Task<ProfileHistoryReplay> ReplayAsync(int maximumItems, CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new ProfileHistoryReplay([], false, false);
        }

        var info = new FileInfo(_path);
        if (info.Length > ProfileHistoryStore.MaximumFileBytes)
        {
            throw new InvalidDataException("The local profile history exceeds its size limit.");
        }

        if (info.Length == 0)
        {
            return new ProfileHistoryReplay([], false, false);
        }

        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var endsWithNewLine = EndsWithNewLine(stream);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        return await ReadLinesAsync(reader, maximumItems, endsWithNewLine, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryAppendAsync(ProfileEvent profileEvent, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(profileEvent, ProfileJson.Options);
        if (bytes.Length > MaximumLineCharacters)
        {
            return false;
        }

        EnsureDirectory();
        await using var stream = new FileStream(
            _path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        if (stream.Length > ProfileHistoryStore.MaximumFileBytes - bytes.Length - NewLine.Length)
        {
            return false;
        }

        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> RewriteAsync(IEnumerable<ProfileEvent> events, CancellationToken cancellationToken)
    {
        var directory = EnsureDirectory();
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            if (!await WriteTemporaryAsync(temporaryPath, events, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            File.Move(temporaryPath, _path, overwrite: true);
            return true;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<ProfileHistoryReplay> ReadLinesAsync(
        StreamReader reader,
        int maximumItems,
        bool endsWithNewLine,
        CancellationToken cancellationToken)
    {
        var events = new List<ProfileEvent>();
        var ids = new HashSet<Guid>();
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        var lineNumber = 0;
        try
        {
            while (line is not null)
            {
                var nextLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                lineNumber++;
                if (line.Length > MaximumLineCharacters)
                {
                    throw new InvalidDataException($"The local profile history line {lineNumber} exceeds its size limit.");
                }

                if (line.Length > 0 && !TryAcceptLine(line, events, ids, maximumItems))
                {
                    if (nextLine is null && !endsWithNewLine)
                    {
                        return new ProfileHistoryReplay(events, true, true);
                    }

                    throw new InvalidDataException($"The local profile history line {lineNumber} is corrupt.");
                }

                line = nextLine;
            }
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The local profile history contains invalid UTF-8.", exception);
        }

        return new ProfileHistoryReplay(events, !endsWithNewLine, false);
    }

    private static bool TryAcceptLine(
        string line,
        List<ProfileEvent> events,
        HashSet<Guid> ids,
        int maximumItems)
    {
        if (!TryParse(line, out var profileEvent))
        {
            return false;
        }

        if (!ids.Add(profileEvent!.EventId))
        {
            return true;
        }

        if (events.Count >= maximumItems)
        {
            throw new InvalidDataException("The local profile history item limit was exceeded.");
        }

        events.Add(profileEvent);
        return true;
    }

    private static bool TryParse(string line, out ProfileEvent? profileEvent)
    {
        profileEvent = null;
        try
        {
            profileEvent = JsonSerializer.Deserialize<ProfileEvent>(line, ProfileJson.Options);
            if (profileEvent is null || !ProfileHistoryProjection.IsHistoricalType(profileEvent.Type))
            {
                return false;
            }

            ProfileEvent.Validate(profileEvent);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return false;
        }
    }

    private static async Task<bool> WriteTemporaryAsync(
        string path,
        IEnumerable<ProfileEvent> events,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        foreach (var profileEvent in events)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(profileEvent, ProfileJson.Options);
            if (bytes.Length > MaximumLineCharacters ||
                stream.Length > ProfileHistoryStore.MaximumFileBytes - bytes.Length - NewLine.Length)
            {
                return false;
            }

            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private string EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The history path has no parent directory.");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static bool EndsWithNewLine(FileStream stream)
    {
        stream.Seek(-1, SeekOrigin.End);
        var result = stream.ReadByte() == '\n';
        stream.Seek(0, SeekOrigin.Begin);
        return result;
    }
}

internal sealed record ProfileHistoryReplay(
    List<ProfileEvent> Events,
    bool RequiresRewrite,
    bool RecoveredTruncatedTail);
