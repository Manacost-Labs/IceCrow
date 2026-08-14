using System.Text.Json;
using System.Text.Json.Serialization;

namespace IceCrow.FixtureTool;

public static class FixtureManifestSerializer
{
    public const long MaximumManifestBytes = 1024 * 1024;
    public const int MaximumCheckpoints = 512;
    public const int MaximumMetadataCharacters = 4096;

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 12,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static async Task<FixtureManifest> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("Fixture manifest was not found.", fullPath);
        }

        if (file.Length > MaximumManifestBytes)
        {
            throw new InvalidDataException(
                $"Fixture manifest exceeds the {MaximumManifestBytes} byte limit.");
        }

        await using var stream = await ReadBoundedAsync(fullPath, cancellationToken)
            .ConfigureAwait(false);
        FixtureManifest? manifest;
        try
        {
            manifest = await JsonSerializer
                .DeserializeAsync<FixtureManifest>(stream, Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Fixture manifest JSON is invalid.", exception);
        }

        Validate(manifest ?? throw new InvalidDataException("Fixture manifest is empty."));
        return manifest;
    }

    private static async Task<MemoryStream> ReadBoundedAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaximumManifestBytes)
            {
                await output.DisposeAsync().ConfigureAwait(false);
                throw new InvalidDataException(
                    $"Fixture manifest exceeds the {MaximumManifestBytes} byte limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        output.Position = 0;
        return output;
    }

    public static async Task SaveAsync(
        string path,
        FixtureManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate(manifest);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ??
                        throw new ArgumentException("Manifest path has no directory.", nameof(path));
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
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer
                    .SerializeAsync(stream, manifest, Options, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (stream.Length > MaximumManifestBytes)
                {
                    throw new InvalidDataException(
                        $"Fixture manifest exceeds the {MaximumManifestBytes} byte limit.");
                }
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

    public static void Validate(FixtureManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != FixtureManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported fixture schemaVersion {manifest.SchemaVersion}.");
        }

        RequireMetadata(manifest.Name, nameof(manifest.Name));
        RequireMetadata(manifest.Reason, nameof(manifest.Reason));
        ValidateOptionalMetadata(manifest.HearthstoneVersion, nameof(manifest.HearthstoneVersion));
        if (manifest.SourceType is not (FixtureSourceTypes.Synthetic or FixtureSourceTypes.RealAnonymized))
        {
            throw new InvalidDataException(
                $"Fixture sourceType must be '{FixtureSourceTypes.Synthetic}' or '{FixtureSourceTypes.RealAnonymized}'.");
        }

        if (manifest.InputType is not (FixtureInputTypes.NormalizedRecording or FixtureInputTypes.RawPowerLog))
        {
            throw new InvalidDataException("Fixture inputType is unsupported.");
        }

        if (manifest.IceCrowFormatVersion <= 0)
        {
            throw new InvalidDataException("Fixture IceCrow format version must be positive.");
        }

        ValidateSimpleFileName(manifest.InputFile);
        if (manifest.InputType == FixtureInputTypes.RawPowerLog && manifest.RawStartedAt is null)
        {
            throw new InvalidDataException("Raw Power fixtures require rawStartedAt.");
        }

        var checkpoints = manifest.ExpectedCheckpoints ??
                          throw new InvalidDataException("Fixture expectedCheckpoints are required.");
        if (checkpoints.Length == 0 || checkpoints.Length > MaximumCheckpoints)
        {
            throw new InvalidDataException(
                $"Fixture must contain between 1 and {MaximumCheckpoints} checkpoints.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var previousIndex = -1;
        foreach (var checkpoint in checkpoints)
        {
            if (checkpoint is null)
            {
                throw new InvalidDataException("Fixture checkpoints cannot contain null entries.");
            }

            RequireMetadata(checkpoint.Name, nameof(checkpoint.Name));
            if (!names.Add(checkpoint.Name))
            {
                throw new InvalidDataException(
                    $"Fixture checkpoint '{checkpoint.Name}' is duplicated.");
            }

            if (checkpoint.EventIndex < 0 || checkpoint.EventIndex < previousIndex)
            {
                throw new InvalidDataException(
                    "Fixture checkpoint indices must be non-negative and ordered.");
            }

            previousIndex = checkpoint.EventIndex;
            ValidateState(checkpoint.State ?? throw new InvalidDataException(
                $"Fixture checkpoint '{checkpoint.Name}' has no state expectation."));
        }
    }

    private static void ValidateState(FixtureStateExpectation state)
    {
        ValidateOptionalMetadata(state.SessionState, nameof(state.SessionState));
        ValidateOptionalMetadata(state.Phase, nameof(state.Phase));
        if (state.Turn < 0 || state.LobbyCount < 0 || state.LocalPlayerId <= 0 ||
            state.CurrentOpponentPlayerId <= 0)
        {
            throw new InvalidDataException("Fixture state expectation contains an invalid numeric value.");
        }

        if (state.OpponentMemory is null)
        {
            return;
        }

        if (state.OpponentMemory.Length > 16)
        {
            throw new InvalidDataException("Fixture opponent memory expectation exceeds 16 players.");
        }

        var players = new HashSet<int>();
        foreach (var opponent in state.OpponentMemory)
        {
            if (opponent is null || opponent.PlayerId <= 0 || opponent.MinionCount is < 0 or > 7 ||
                opponent.LastSeenTurn < 0 || !players.Add(opponent.PlayerId))
            {
                throw new InvalidDataException("Fixture opponent memory expectation is invalid.");
            }
        }
    }

    private static void ValidateSimpleFileName(string inputFile)
    {
        RequireMetadata(inputFile, nameof(inputFile));
        if (!string.Equals(Path.GetFileName(inputFile), inputFile, StringComparison.Ordinal) ||
            inputFile is "." or ".." ||
            inputFile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("Fixture inputFile must be one plain file name.");
        }
    }

    private static void RequireMetadata(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Fixture field '{fieldName}' cannot be empty.");
        }

        ValidateOptionalMetadata(value, fieldName);
    }

    private static void ValidateOptionalMetadata(string? value, string fieldName)
    {
        if (value?.Length > MaximumMetadataCharacters)
        {
            throw new InvalidDataException(
                $"Fixture field '{fieldName}' exceeds {MaximumMetadataCharacters} characters.");
        }
    }
}
