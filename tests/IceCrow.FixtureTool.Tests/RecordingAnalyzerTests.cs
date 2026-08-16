using System.Globalization;
using IceCrow.FixtureTool;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Recording;

namespace IceCrow.FixtureTool.Tests;

public sealed class RecordingAnalyzerTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        16,
        20,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void AnalysisNeverPrintsEntityNamesOrUnsafeValues()
    {
        var recorder = new MatchRecorder(Timestamp);
        recorder.RecordMatchStarted(Timestamp, localPlayerId: 4);
        recorder.Record(new RawTagChanged(
            Timestamp.AddSeconds(1),
            BlockId: null,
            EntityId: null,
            EntityName: "SecretTag#1234",
            Tag: "TURN",
            Value: "1",
            IsCreationTag: false));
        recorder.Record(new RawTagChanged(
            Timestamp.AddSeconds(2),
            BlockId: null,
            EntityId: 7,
            EntityName: "Секретный Игрок",
            Tag: "PLAYSTATE",
            Value: "WON",
            IsCreationTag: false));
        recorder.Record(new RawTagChanged(
            Timestamp.AddSeconds(3),
            BlockId: null,
            EntityId: 7,
            EntityName: null,
            Tag: "2022",
            Value: "with spaces and #",
            IsCreationTag: false));
        recorder.RecordMatchEnded(Timestamp.AddSeconds(4));
        var match = recorder.CreateMatch();

        var report = RecordingAnalyzer.Analyze(
            match,
            new RecordingAnalysisOptions(FocusTag: "2022", AroundEvent: 2, AroundWindow: 5));

        Assert.DoesNotContain("SecretTag", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Секретный", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("with spaces", report, StringComparison.Ordinal);
        Assert.Contains("named-only", report, StringComparison.Ordinal);
        Assert.Contains("<redacted>", report, StringComparison.Ordinal);
        Assert.Contains("Tag 'TURN' occurrences:", report, StringComparison.Ordinal);
        Assert.Contains("value=1", report, StringComparison.Ordinal);
        Assert.Contains("Tag '2022' occurrences:", report, StringComparison.Ordinal);
        Assert.Contains("numeric-id=2 named-only=1", report, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalysisReportsTypeCountsAndTagFrequencies()
    {
        var recorder = new MatchRecorder(Timestamp);
        recorder.RecordMatchStarted(Timestamp, localPlayerId: 1);
        for (var index = 0; index < 5; index++)
        {
            recorder.Record(new RawTagChanged(
                Timestamp.AddSeconds(index),
                BlockId: null,
                EntityId: 1,
                EntityName: null,
                Tag: "TURN",
                Value: (index + 1).ToString(CultureInfo.InvariantCulture),
                IsCreationTag: false));
        }

        recorder.RecordMatchEnded(Timestamp.AddMinutes(1));

        var report = RecordingAnalyzer.Analyze(
            recorder.CreateMatch(),
            new RecordingAnalysisOptions());

        Assert.Contains("Events: 7", report, StringComparison.Ordinal);
        Assert.Contains("RawTagChanged", report, StringComparison.Ordinal);
        Assert.Contains("TURN", report, StringComparison.Ordinal);
        Assert.Contains("[     1]", report, StringComparison.Ordinal);
    }
}
