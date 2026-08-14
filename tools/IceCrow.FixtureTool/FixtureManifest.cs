using System.Collections.ObjectModel;

namespace IceCrow.FixtureTool;

public static class FixtureSourceTypes
{
    public const string Synthetic = "synthetic";
    public const string RealAnonymized = "real-anonymized";
}

public static class FixtureInputTypes
{
    public const string NormalizedRecording = "normalized-recording";
    public const string RawPowerLog = "raw-power-log";
}

public sealed record FixtureManifest
{
    public const int CurrentSchemaVersion = 1;

    public required int SchemaVersion { get; init; }

    public required string Name { get; init; }

    public required string SourceType { get; init; }

    public string? HearthstoneVersion { get; init; }

    public required int IceCrowFormatVersion { get; init; }

    public required string Reason { get; init; }

    public required string InputType { get; init; }

    public required string InputFile { get; init; }

    public DateTimeOffset? RawStartedAt { get; init; }

    public required FixtureCheckpointExpectation[] ExpectedCheckpoints { get; init; }
}

public sealed record FixtureCheckpointExpectation
{
    public required string Name { get; init; }

    public required int EventIndex { get; init; }

    public required FixtureStateExpectation State { get; init; }
}

public sealed record FixtureStateExpectation
{
    public string? SessionState { get; init; }

    public bool? IsActive { get; init; }

    public int? Turn { get; init; }

    public string? Phase { get; init; }

    public int? LocalPlayerId { get; init; }

    public int? CurrentOpponentPlayerId { get; init; }

    public int? LobbyCount { get; init; }

    public FixtureOpponentExpectation[]? OpponentMemory { get; init; }
}

public sealed record FixtureOpponentExpectation
{
    public required int PlayerId { get; init; }

    public required int MinionCount { get; init; }

    public int? LastSeenTurn { get; init; }
}

public sealed record FixtureRunResult(
    string FixtureName,
    string SourceType,
    string InputType,
    IReadOnlyList<string> Checkpoints)
{
    public static FixtureRunResult Create(
        string fixtureName,
        string sourceType,
        string inputType,
        IEnumerable<string> checkpoints) => new(
            fixtureName,
            sourceType,
            inputType,
            new ReadOnlyCollection<string>(checkpoints.ToArray()));
}
