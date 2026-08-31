using IceCrow.FixtureTool;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Recording;

namespace IceCrow.FixtureTool.Tests;

public sealed class RecordingValidatorTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        17,
        18,
        0,
        0,
        TimeSpan.Zero);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"icecrow-validator-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task SummaryReportsIdentityFreeCaptureMetadata()
    {
        var recorder = new MatchRecorder(Timestamp);
        recorder.RecordMatchStarted(Timestamp, localPlayerId: 1);
        recorder.Record(new RawTagChanged(
            Timestamp.AddSeconds(1),
            BlockId: null,
            EntityId: 1,
            EntityName: null,
            Tag: "PLAYER_TECH_LEVEL",
            Value: "2",
            IsCreationTag: false));
        recorder.RecordMatchEnded(Timestamp.AddMinutes(20));
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "capture.icecrow.json");
        await RecordingSerializer.SaveAsync(path, recorder.CreateMatch());

        var summary = await RecordingValidator.ValidateAsync(path);

        Assert.Contains("OFFICIAL VALIDATION PASSED", summary, StringComparison.Ordinal);
        Assert.Contains("format version       : 1", summary, StringComparison.Ordinal);
        Assert.Contains("capture started      : 2026-08-17 18:00:00.000", summary, StringComparison.Ordinal);
        Assert.Contains("capture ended        : 2026-08-17 18:20:00.000", summary, StringComparison.Ordinal);
        Assert.Contains("events               : 3", summary, StringComparison.Ordinal);
        Assert.Contains("replayed             : 3", summary, StringComparison.Ordinal);
        Assert.Contains("unresolved named refs: 0", summary, StringComparison.Ordinal);
        Assert.Contains("event snapshot work  : ", summary, StringComparison.Ordinal);
        Assert.Contains("timeline work        : ", summary, StringComparison.Ordinal);
        Assert.Contains("max tags/entity      : ", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayWorkMeasurementReportsCountersWithoutIdentities()
    {
        var recorder = new MatchRecorder(Timestamp);
        recorder.RecordMatchStarted(Timestamp, localPlayerId: 1);
        recorder.Record(new RawTagChanged(
            Timestamp.AddSeconds(1),
            BlockId: null,
            EntityId: 1,
            EntityName: null,
            Tag: "PLAYER_TECH_LEVEL",
            Value: "2",
            IsCreationTag: false));
        recorder.RecordMatchEnded(Timestamp.AddMinutes(20));
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "capture.icecrow.json");
        await RecordingSerializer.SaveAsync(path, recorder.CreateMatch());

        var report = await RecordingValidator.AnalyzeReplayWorkAsync(path);

        Assert.Contains("REPLAY WORK MEASUREMENT", report, StringComparison.Ordinal);
        Assert.Contains("events processed     : 3", report, StringComparison.Ordinal);
        Assert.Contains("work per event       : ", report, StringComparison.Ordinal);
        Assert.Contains("replay elapsed       : ", report, StringComparison.Ordinal);
    }
}
