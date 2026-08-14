using IceCrow.Battlegrounds.Memory;
using IceCrow.Hearthstone.Logs;
using IceCrow.Live;
using IceCrow.Recording;
using System.Text;

namespace IceCrow.FixtureTool;

public static class FixtureGoldenRunner
{
    public const long MaximumRawFixtureBytes = 4L * 1024 * 1024;
    public const int MaximumRawLines = 10_000;
    public const int MaximumRawLineCharacters = 128 * 1024;
    public const int MaximumCorpusFixtures = 256;

    public static IEnumerable<string> EnumerateFixtureDirectories(string corpusDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(corpusDirectory);
        var root = Path.GetFullPath(corpusDirectory);
        if (!Directory.Exists(root))
        {
            return [];
        }

        var directories = Directory
            .EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Where(directory => File.Exists(Path.Combine(directory, "expected.json")))
            .Order(StringComparer.Ordinal)
            .Take(MaximumCorpusFixtures + 1)
            .ToArray();
        if (directories.Length > MaximumCorpusFixtures)
        {
            throw new InvalidDataException(
                $"Corpus exceeds the {MaximumCorpusFixtures} fixture limit.");
        }

        return directories;
    }

    public static async Task<FixtureRunResult> RunAsync(
        string fixtureDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureDirectory);
        var root = Path.GetFullPath(fixtureDirectory);
        var manifest = await FixtureManifestSerializer
            .LoadAsync(Path.Combine(root, "expected.json"), cancellationToken)
            .ConfigureAwait(false);
        var inputPath = ResolveContainedInput(root, manifest.InputFile);

        return manifest.InputType switch
        {
            FixtureInputTypes.NormalizedRecording => await RunRecordingAsync(
                inputPath,
                manifest,
                cancellationToken).ConfigureAwait(false),
            FixtureInputTypes.RawPowerLog => await RunRawAsync(
                inputPath,
                manifest,
                cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidDataException(
                $"Fixture '{manifest.Name}' has unsupported inputType '{manifest.InputType}'."),
        };
    }

    public static async Task<IReadOnlyList<FixtureRunResult>> RunCorpusAsync(
        string corpusDirectory,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FixtureRunResult>();
        foreach (var fixtureDirectory in EnumerateFixtureDirectories(corpusDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunAsync(fixtureDirectory, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private static async Task<FixtureRunResult> RunRecordingAsync(
        string inputPath,
        FixtureManifest manifest,
        CancellationToken cancellationToken)
    {
        var match = await RecordingSerializer.LoadAsync(inputPath, cancellationToken)
            .ConfigureAwait(false);
        if (match.FormatVersion != manifest.IceCrowFormatVersion)
        {
            throw new InvalidDataException(
                $"Fixture '{manifest.Name}' expects IceCrow format {manifest.IceCrowFormatVersion}, " +
                $"but recording uses {match.FormatVersion}.");
        }

        var runner = new ReplayRunner(match);
        var passed = new List<string>();
        foreach (var checkpoint in manifest.ExpectedCheckpoints)
        {
            if (checkpoint.EventIndex >= match.Events.Count)
            {
                throw new InvalidDataException(
                    $"Fixture '{manifest.Name}' checkpoint '{checkpoint.Name}' points beyond the recording.");
            }

            var actual = FixtureExpectedStateBuilder.FromReplayState(
                runner.RunUntilEvent(checkpoint.EventIndex, cancellationToken));
            AssertState(manifest.Name, checkpoint, actual);
            passed.Add(checkpoint.Name);
        }

        return FixtureRunResult.Create(
            manifest.Name,
            manifest.SourceType,
            manifest.InputType,
            passed);
    }

    private static async Task<FixtureRunResult> RunRawAsync(
        string inputPath,
        FixtureManifest manifest,
        CancellationToken cancellationToken)
    {
        var lines = await ReadRawLinesAsync(inputPath, cancellationToken).ConfigureAwait(false);
        var coordinator = new LiveTrackingCoordinator();
        var passed = new List<string>();
        var checkpointIndex = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var timestamp = manifest.RawStartedAt!.Value.AddMilliseconds(index);
            _ = coordinator.Process(new RawLogLine(
                timestamp,
                "Power",
                $"PowerTaskList.DebugPrintPower() - {lines[index]}",
                lines[index]));

            while (checkpointIndex < manifest.ExpectedCheckpoints.Length &&
                   manifest.ExpectedCheckpoints[checkpointIndex].EventIndex == index)
            {
                var checkpoint = manifest.ExpectedCheckpoints[checkpointIndex++];
                var actual = FixtureExpectedStateBuilder.FromTrackingSnapshot(
                    coordinator.CurrentSnapshot);
                AssertState(manifest.Name, checkpoint, actual);
                passed.Add(checkpoint.Name);
            }
        }

        if (checkpointIndex != manifest.ExpectedCheckpoints.Length)
        {
            var missing = manifest.ExpectedCheckpoints[checkpointIndex];
            throw new InvalidDataException(
                $"Fixture '{manifest.Name}' checkpoint '{missing.Name}' points beyond the raw fixture.");
        }

        return FixtureRunResult.Create(
            manifest.Name,
            manifest.SourceType,
            manifest.InputType,
            passed);
    }

    private static async Task<IReadOnlyList<string>> ReadRawLinesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var contents = await ReadFileBoundedAsync(path, cancellationToken).ConfigureAwait(false);
        var lines = new List<string>();
        using var reader = new StreamReader(
            new MemoryStream(contents, writable: false),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                throw new InvalidDataException("Raw Power fixtures cannot contain blank lines.");
            }

            if (line.Length > MaximumRawLineCharacters)
            {
                throw new InvalidDataException(
                    $"Raw Power fixture line exceeds {MaximumRawLineCharacters} characters.");
            }

            if (lines.Count >= MaximumRawLines)
            {
                throw new InvalidDataException(
                    $"Raw Power fixture exceeds the {MaximumRawLines} line limit.");
            }

            lines.Add(line);
        }

        return lines;
    }

    private static async Task<byte[]> ReadFileBoundedAsync(
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
        await using var output = new MemoryStream();
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
            if (total > MaximumRawFixtureBytes)
            {
                throw new InvalidDataException(
                    $"Raw Power fixture exceeds the {MaximumRawFixtureBytes} byte limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private static string ResolveContainedInput(string root, string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(root, fileName));
        var relative = Path.GetRelativePath(root, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException("Fixture input path escapes its fixture directory.");
        }

        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException("Fixture input files cannot be filesystem links.");
        }

        return path;
    }

    private static void AssertState(
        string fixtureName,
        FixtureCheckpointExpectation checkpoint,
        FixtureStateExpectation actual)
    {
        var expected = checkpoint.State;
        AssertValue(fixtureName, checkpoint.Name, "SessionState", expected.SessionState, actual.SessionState);
        AssertValue(fixtureName, checkpoint.Name, "IsActive", expected.IsActive, actual.IsActive);
        AssertValue(fixtureName, checkpoint.Name, "Turn", expected.Turn, actual.Turn);
        AssertValue(fixtureName, checkpoint.Name, "Phase", expected.Phase, actual.Phase);
        AssertValue(fixtureName, checkpoint.Name, "LocalPlayerId", expected.LocalPlayerId, actual.LocalPlayerId);
        AssertValue(
            fixtureName,
            checkpoint.Name,
            "CurrentOpponentPlayerId",
            expected.CurrentOpponentPlayerId,
            actual.CurrentOpponentPlayerId);
        AssertValue(fixtureName, checkpoint.Name, "LobbyCount", expected.LobbyCount, actual.LobbyCount);

        if (expected.OpponentMemory is null)
        {
            return;
        }

        var actualByPlayer = (actual.OpponentMemory ?? [])
            .ToDictionary(static opponent => opponent.PlayerId);
        foreach (var opponent in expected.OpponentMemory)
        {
            if (!actualByPlayer.TryGetValue(opponent.PlayerId, out var actualOpponent))
            {
                throw Failure(
                    fixtureName,
                    checkpoint.Name,
                    $"OpponentMemory[{opponent.PlayerId}]",
                    "present",
                    "missing");
            }

            AssertValue(
                fixtureName,
                checkpoint.Name,
                $"OpponentMemory[{opponent.PlayerId}].Minions",
                opponent.MinionCount,
                actualOpponent.MinionCount);
            AssertValue(
                fixtureName,
                checkpoint.Name,
                $"OpponentMemory[{opponent.PlayerId}].LastSeenTurn",
                opponent.LastSeenTurn,
                actualOpponent.LastSeenTurn);
        }
    }

    private static void AssertValue<T>(
        string fixture,
        string checkpoint,
        string field,
        T? expected,
        T? actual)
    {
        if (expected is not null && !EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw Failure(fixture, checkpoint, field, expected, actual);
        }
    }

    private static FixtureAssertionException Failure(
        string fixture,
        string checkpoint,
        string field,
        object? expected,
        object? actual) => new(
            $"Fixture: {fixture}{Environment.NewLine}" +
            $"Checkpoint: {checkpoint}{Environment.NewLine}" +
            $"Expected {field}: {expected ?? "<null>"}{Environment.NewLine}" +
            $"Actual {field}: {actual ?? "<null>"}");
}

public sealed class FixtureAssertionException(string message) : Exception(message);
