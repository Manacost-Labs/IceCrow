using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IceCrow.Recording;

public static class RecordingSerializer
{
    // Calibrated against real matches (2026-08-17 live session): a solo
    // Battlegrounds match had ~44k applied events by turn 6 and exceeded
    // 100k before its natural end, discarding the capture. A full late-game
    // match projects to ~150-200k events and ~40-60 MB of indented JSON, so
    // the budgets carry roughly 2x headroom above that projection.
    public const int MaximumEventCount = 250_000;
    public const int MaximumCheckpointCount = 4_096;
    public const int MaximumStringCharacters = 256 * 1024;
    public const long MaximumFileBytes = 128L * 1024 * 1024;
    public const long MaximumRetainedBytes = 96L * 1024 * 1024;

    // Write/read contract: any match the write path accepts must load again.
    // The write path budgets MaximumRetainedBytes with a per-event model
    // (256 bytes base + 2 bytes per string character). The read preflight
    // instead charges every JSON token, which costs more for the same data:
    // schema property names, per-token overhead, and primitive fields add up
    // to at most ~4x the write-side base, and the serializer escapes
    // non-ASCII text as \uXXXX, inflating an encoded string to at most 6
    // bytes per character versus the 2 bytes the writer charged (3x). The
    // preflight budget therefore allows 8x the retained budget: large enough
    // that no writer-accepted recording is ever rejected, small enough that
    // hostile token floods in a 64 MiB file still fail fast.
    public const long MaximumPreflightMaterializationBytes = 8 * MaximumRetainedBytes;

    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public static async Task SaveAsync(
        string path,
        RecordedMatch match,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(match);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ??
                        throw new ArgumentException("Recording path has no directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
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
                await SerializeAsync(stream, match, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static async Task<RecordedMatch> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = new FileStream(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public static async Task SerializeAsync(
        Stream destination,
        RecordedMatch match,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(match);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("Destination stream must be writable.", nameof(destination));
        }

        Validate(match);
        var envelope = new RecordingEnvelope
        {
            FormatVersion = match.FormatVersion,
            StartedAt = match.StartedAt,
            Events = match.Events.ToArray(),
            Checkpoints = match.Checkpoints.ToArray(),
        };
        await using var bounded = new BoundedWriteStream(
            destination,
            MaximumFileBytes,
            leaveOpen: true);
        await JsonSerializer
            .SerializeAsync(bounded, envelope, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        await bounded.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<RecordedMatch> DeserializeAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("Source stream must be readable.", nameof(source));
        }

        await using var boundedInput = await ReadBoundedAsync(source, cancellationToken)
            .ConfigureAwait(false);
        PreflightJson(boundedInput);
        RecordingEnvelope? envelope;
        try
        {
            envelope = await JsonSerializer
                .DeserializeAsync<RecordingEnvelope>(
                    boundedInput,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Recording JSON is invalid.", exception);
        }

        if (envelope?.Events is null)
        {
            throw new InvalidDataException("Recording envelope is missing its events array.");
        }

        var events = envelope.Events
            .Select(recordedEvent => recordedEvent ?? throw new InvalidDataException(
                "Recording events cannot contain null entries."))
            .ToArray();
        var checkpoints = envelope.Checkpoints?
            .Select(checkpoint => checkpoint ?? throw new InvalidDataException(
                "Recording checkpoints cannot contain null entries."))
            .ToArray();
        var match = new RecordedMatch(
            envelope.FormatVersion,
            envelope.StartedAt,
            events,
            checkpoints);
        Validate(match);
        return match;
    }

    public static void Validate(RecordedMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (match.FormatVersion != RecordedMatch.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported recording formatVersion {match.FormatVersion}.");
        }

        if (match.Events.Count > MaximumEventCount)
        {
            throw new InvalidDataException(
                $"Recording exceeds the {MaximumEventCount} event limit.");
        }

        if (match.Checkpoints.Count > MaximumCheckpointCount)
        {
            throw new InvalidDataException(
                $"Recording exceeds the {MaximumCheckpointCount} checkpoint limit.");
        }

        long retainedBytes = 0;
        foreach (var recordedEvent in match.Events)
        {
            ValidateEvent(recordedEvent);
            ReserveRetainedBytes(ref retainedBytes, EstimateRetainedBytes(recordedEvent));
        }

        ValidateLifecycle(match.Events);

        var checkpointNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var checkpoint in match.Checkpoints)
        {
            ValidateCheckpoint(checkpoint, match.Events.Count);
            ReserveRetainedBytes(ref retainedBytes, EstimateRetainedBytes(checkpoint));
            if (!checkpointNames.Add(checkpoint.Name))
            {
                throw new InvalidDataException(
                    $"Recording contains duplicate checkpoint '{checkpoint.Name}'.");
            }
        }
    }

    internal static void ValidateEvent(RecordedEvent recordedEvent)
    {
        ArgumentNullException.ThrowIfNull(recordedEvent);
        if (!Enum.IsDefined(recordedEvent.Type))
        {
            throw new InvalidDataException(
                $"Unsupported recorded event type '{recordedEvent.Type}'.");
        }

        ValidatePositiveOptional(recordedEvent.EntityId, nameof(recordedEvent.EntityId));
        ValidatePositiveOptional(recordedEvent.PlayerId, nameof(recordedEvent.PlayerId));
        ValidateOptionalString(recordedEvent.EntityName, nameof(recordedEvent.EntityName));
        ValidateOptionalString(recordedEvent.CardId, nameof(recordedEvent.CardId));
        ValidateOptionalString(recordedEvent.GameAccountId, nameof(recordedEvent.GameAccountId));
        ValidateOptionalString(recordedEvent.Tag, nameof(recordedEvent.Tag));
        ValidateOptionalString(recordedEvent.Value, nameof(recordedEvent.Value));
        ValidateOptionalString(recordedEvent.Content, nameof(recordedEvent.Content));

        switch (recordedEvent.Type)
        {
            case RecordedEventType.MatchStarted:
            case RecordedEventType.MatchEnded:
            case RecordedEventType.GameCreated:
                break;
            case RecordedEventType.GameEntityDeclared:
                RequireValue(recordedEvent.EntityId, nameof(recordedEvent.EntityId));
                break;
            case RecordedEventType.PlayerEntityDeclared:
                RequireValue(recordedEvent.EntityId, nameof(recordedEvent.EntityId));
                RequireValue(recordedEvent.PlayerId, nameof(recordedEvent.PlayerId));
                RequireReference(recordedEvent.GameAccountId, nameof(recordedEvent.GameAccountId));
                break;
            case RecordedEventType.EntityCreated:
                RequireValue(recordedEvent.EntityId, nameof(recordedEvent.EntityId));
                RequireReference(recordedEvent.CardId, nameof(recordedEvent.CardId));
                break;
            case RecordedEventType.EntityRevealed:
            case RecordedEventType.EntityChanged:
                RequireEntityReference(recordedEvent);
                RequireReference(recordedEvent.CardId, nameof(recordedEvent.CardId));
                break;
            case RecordedEventType.RawTagChanged:
                RequireEntityReference(recordedEvent);
                RequireReference(recordedEvent.Tag, nameof(recordedEvent.Tag));
                RequireReference(recordedEvent.Value, nameof(recordedEvent.Value));
                RequireValue(recordedEvent.IsCreationTag, nameof(recordedEvent.IsCreationTag));
                break;
            case RecordedEventType.BlockStarted:
            case RecordedEventType.BlockEnded:
                ValidateBlock(RequireReference(recordedEvent.Block, nameof(recordedEvent.Block)));
                break;
            case RecordedEventType.UnknownPower:
                RequireReference(recordedEvent.Content, nameof(recordedEvent.Content));
                break;
            default:
                throw new InvalidDataException(
                    $"Unsupported recorded event type '{recordedEvent.Type}'.");
        }
    }

    internal static void ValidateCheckpoint(ReplayCheckpoint checkpoint, int eventCount)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (string.IsNullOrWhiteSpace(checkpoint.Name))
        {
            throw new InvalidDataException("Checkpoint name cannot be empty.");
        }

        ValidateOptionalString(checkpoint.Name, nameof(checkpoint.Name));
        if (checkpoint.EventIndex < 0 || checkpoint.EventIndex >= eventCount)
        {
            throw new InvalidDataException(
                $"Checkpoint '{checkpoint.Name}' has an invalid event index.");
        }
    }

    internal static long EstimateRetainedBytes(RecordedEvent recordedEvent)
    {
        ArgumentNullException.ThrowIfNull(recordedEvent);

        return 256L +
               EstimateString(recordedEvent.EntityName) +
               EstimateString(recordedEvent.CardId) +
               EstimateString(recordedEvent.GameAccountId) +
               EstimateString(recordedEvent.Tag) +
               EstimateString(recordedEvent.Value) +
               EstimateString(recordedEvent.Content) +
               EstimateBlock(recordedEvent.Block);
    }

    internal static long EstimateRetainedBytes(ReplayCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return 64L + EstimateString(checkpoint.Name);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 16,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
        };
        options.Converters.Add(
            new JsonStringEnumConverter<RecordedEventType>(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false));
        return options;
    }

    private static void ValidateLifecycle(IReadOnlyList<RecordedEvent> events)
    {
        if (events.Count == 0 || events[0].Type != RecordedEventType.MatchStarted)
        {
            throw new InvalidDataException(
                "A recording must start with exactly one MatchStarted event.");
        }

        var ended = false;
        for (var index = 1; index < events.Count; index++)
        {
            var type = events[index].Type;
            if (type == RecordedEventType.MatchStarted)
            {
                throw new InvalidDataException(
                    "A recording cannot contain multiple MatchStarted events.");
            }

            if (ended)
            {
                throw new InvalidDataException(
                    "No event can follow MatchEnded in a recording.");
            }

            ended = type == RecordedEventType.MatchEnded;
        }
    }

    private static long EstimateBlock(RecordedPowerBlock? block) => block is null
        ? 0
        : 128L +
          EstimateString(block.Type) +
          EstimateString(block.EntityName) +
          EstimateString(block.EffectCardId) +
          EstimateString(block.Target) +
          EstimateString(block.TriggerKeyword);

    private static long EstimateString(string? value) => value is null
        ? 0
        : Math.Max(
            (long)value.Length * sizeof(char),
            Encoding.UTF8.GetByteCount(value));

    private static void ReserveRetainedBytes(ref long retainedBytes, long byteCount)
    {
        if (byteCount < 0 || retainedBytes > MaximumRetainedBytes - byteCount)
        {
            throw new InvalidDataException(
                $"Recording exceeds the {MaximumRetainedBytes} estimated retained-byte limit.");
        }

        retainedBytes += byteCount;
    }

    private static async Task<MemoryStream> ReadBoundedAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        if (source.CanSeek && source.Length - source.Position > MaximumFileBytes)
        {
            throw new InvalidDataException(
                $"Recording exceeds the {MaximumFileBytes} byte limit.");
        }

        var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source
                .ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaximumFileBytes)
            {
                await output.DisposeAsync().ConfigureAwait(false);
                throw new InvalidDataException(
                    $"Recording exceeds the {MaximumFileBytes} byte limit.");
            }

            await output
                .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        output.Position = 0;
        return output;
    }

    private static void PreflightJson(MemoryStream input)
    {
        if (!input.TryGetBuffer(out var buffer))
        {
            throw new InvalidDataException("Recording buffer is unavailable.");
        }

        var reader = new Utf8JsonReader(
            buffer.AsSpan(0, checked((int)input.Length)),
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
        var firstRootPropertySeen = false;
        var formatVersionSeen = false;
        var startedAtSeen = false;
        var eventsSeen = false;
        var checkpointsSeen = false;
        var expectingFormatVersion = false;
        var expectingStartedAt = false;
        var expectingEvents = false;
        var expectingCheckpoints = false;
        var eventsArrayDepth = -1;
        var checkpointsArrayDepth = -1;
        var eventCount = 0;
        var checkpointCount = 0;
        long estimatedMaterializedBytes = 0;

        try
        {
            while (reader.Read())
            {
                ReservePreflightMaterialization(
                    ref estimatedMaterializedBytes,
                    EstimateJsonTokenMaterialization(reader));

                if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName &&
                    reader.ValueSpan.Length > MaximumStringCharacters * 6)
                {
                    throw new InvalidDataException(
                        "Recording contains an excessively large encoded string.");
                }

                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
                {
                    if (!firstRootPropertySeen)
                    {
                        firstRootPropertySeen = true;
                        if (!reader.ValueTextEquals("formatVersion"u8))
                        {
                            throw new InvalidDataException(
                                "formatVersion must be the first recording property.");
                        }
                    }

                    expectingFormatVersion = reader.ValueTextEquals("formatVersion"u8);
                    expectingStartedAt = reader.ValueTextEquals("startedAt"u8);
                    expectingEvents = reader.ValueTextEquals("events"u8);
                    expectingCheckpoints = reader.ValueTextEquals("checkpoints"u8);
                    continue;
                }

                if (expectingFormatVersion)
                {
                    expectingFormatVersion = false;
                    if (formatVersionSeen ||
                        reader.TokenType != JsonTokenType.Number ||
                        !reader.TryGetInt32(out var formatVersion))
                    {
                        throw new InvalidDataException("Recording formatVersion is invalid.");
                    }

                    formatVersionSeen = true;
                    if (formatVersion != RecordedMatch.CurrentFormatVersion)
                    {
                        throw new InvalidDataException(
                            $"Unsupported recording formatVersion {formatVersion}.");
                    }

                    continue;
                }

                if (expectingStartedAt)
                {
                    expectingStartedAt = false;
                    if (startedAtSeen)
                    {
                        throw new InvalidDataException("Recording contains duplicate startedAt properties.");
                    }

                    startedAtSeen = true;
                    continue;
                }

                if (expectingEvents)
                {
                    expectingEvents = false;
                    if (eventsSeen || reader.TokenType != JsonTokenType.StartArray)
                    {
                        throw new InvalidDataException("Recording events must be one JSON array.");
                    }

                    eventsSeen = true;
                    eventsArrayDepth = reader.CurrentDepth;
                    continue;
                }

                if (expectingCheckpoints)
                {
                    expectingCheckpoints = false;
                    if (checkpointsSeen || reader.TokenType != JsonTokenType.StartArray)
                    {
                        throw new InvalidDataException("Recording checkpoints must be one JSON array.");
                    }

                    checkpointsSeen = true;
                    checkpointsArrayDepth = reader.CurrentDepth;
                    continue;
                }

                if (eventsArrayDepth >= 0)
                {
                    if (reader.TokenType == JsonTokenType.EndArray &&
                        reader.CurrentDepth == eventsArrayDepth)
                    {
                        eventsArrayDepth = -1;
                    }
                    else if (reader.CurrentDepth == eventsArrayDepth + 1 &&
                             StartsJsonValue(reader.TokenType) &&
                             ++eventCount > MaximumEventCount)
                    {
                        throw new InvalidDataException(
                            $"Recording exceeds the {MaximumEventCount} event limit.");
                    }
                }

                if (checkpointsArrayDepth >= 0)
                {
                    if (reader.TokenType == JsonTokenType.EndArray &&
                        reader.CurrentDepth == checkpointsArrayDepth)
                    {
                        checkpointsArrayDepth = -1;
                    }
                    else if (reader.CurrentDepth == checkpointsArrayDepth + 1 &&
                             StartsJsonValue(reader.TokenType) &&
                             ++checkpointCount > MaximumCheckpointCount)
                    {
                        throw new InvalidDataException(
                            $"Recording exceeds the {MaximumCheckpointCount} checkpoint limit.");
                    }
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Recording JSON is invalid.", exception);
        }

        if (!formatVersionSeen || !startedAtSeen || !eventsSeen)
        {
            throw new InvalidDataException(
                "Recording envelope requires formatVersion, startedAt, and events.");
        }

        input.Position = 0;
    }

    private static bool StartsJsonValue(JsonTokenType tokenType) => tokenType is
        JsonTokenType.StartObject or
        JsonTokenType.StartArray or
        JsonTokenType.String or
        JsonTokenType.Number or
        JsonTokenType.True or
        JsonTokenType.False or
        JsonTokenType.Null;

    private static long EstimateJsonTokenMaterialization(Utf8JsonReader reader) =>
        reader.TokenType switch
        {
            JsonTokenType.PropertyName or JsonTokenType.String =>
                32L + (reader.ValueSpan.Length * sizeof(char)),
            JsonTokenType.StartObject => 128,
            JsonTokenType.StartArray => 64,
            JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False or JsonTokenType.Null => 32,
            _ => 0,
        };

    private static void ReservePreflightMaterialization(ref long retainedBytes, long byteCount)
    {
        if (byteCount < 0 ||
            retainedBytes > MaximumPreflightMaterializationBytes - byteCount)
        {
            throw new InvalidDataException(
                $"Recording exceeds the {MaximumPreflightMaterializationBytes} preflight materialization-byte limit.");
        }

        retainedBytes += byteCount;
    }

    private static void ValidateBlock(RecordedPowerBlock block)
    {
        if (block.Depth < 0 || block.Depth > 1_024)
        {
            throw new InvalidDataException("Recorded block depth is out of range.");
        }

        ValidatePositiveOptional(block.EntityId, nameof(block.EntityId));
        RequireReference(block.Type, nameof(block.Type));
        RequireReference(block.EffectCardId, nameof(block.EffectCardId));
        RequireReference(block.Target, nameof(block.Target));
        ValidateOptionalString(block.EntityName, nameof(block.EntityName));
        ValidateOptionalString(block.Type, nameof(block.Type));
        ValidateOptionalString(block.EffectCardId, nameof(block.EffectCardId));
        ValidateOptionalString(block.Target, nameof(block.Target));
        ValidateOptionalString(block.TriggerKeyword, nameof(block.TriggerKeyword));
    }

    private static void RequireEntityReference(RecordedEvent recordedEvent)
    {
        if (recordedEvent.EntityId is null &&
            string.IsNullOrWhiteSpace(recordedEvent.EntityName))
        {
            throw new InvalidDataException(
                "Recorded event requires a numeric entity id or entity name.");
        }
    }

    private static T RequireReference<T>(T? value, string fieldName)
        where T : class => value ?? throw new InvalidDataException(
        $"Recorded event is missing required field '{fieldName}'.");

    private static T RequireValue<T>(T? value, string fieldName)
        where T : struct => value ?? throw new InvalidDataException(
        $"Recorded event is missing required field '{fieldName}'.");

    private static void ValidatePositiveOptional(int? value, string fieldName)
    {
        if (value <= 0)
        {
            throw new InvalidDataException(
                $"Recorded field '{fieldName}' must be positive when present.");
        }
    }

    private static void ValidateOptionalString(string? value, string fieldName)
    {
        if (value?.Length > MaximumStringCharacters)
        {
            throw new InvalidDataException(
                $"Recorded field '{fieldName}' exceeds the {MaximumStringCharacters} character limit.");
        }
    }

    private sealed class RecordingEnvelope
    {
        public int FormatVersion { get; init; }

        public DateTimeOffset StartedAt { get; init; }

        public RecordedEvent?[]? Events { get; init; }

        public ReplayCheckpoint?[]? Checkpoints { get; init; }
    }

    private sealed class BoundedWriteStream(
        Stream inner,
        long maximumBytes,
        bool leaveOpen) : Stream
    {
        private long _bytesWritten;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _bytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
        {
            Reserve(count);
            inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Reserve(buffer.Length);
            inner.Write(buffer);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Reserve(buffer.Length);
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Reserve(int byteCount)
        {
            if (_bytesWritten > maximumBytes - byteCount)
            {
                throw new InvalidDataException(
                    $"Recording exceeds the {maximumBytes} byte limit.");
            }

            _bytesWritten += byteCount;
        }
    }
}
