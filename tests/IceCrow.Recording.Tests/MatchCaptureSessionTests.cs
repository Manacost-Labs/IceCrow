using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Recording.Tests;

public sealed class MatchCaptureSessionTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        15,
        11,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void CompleteMatchProducesOneValidatedRecording()
    {
        var session = new MatchCaptureSession();

        session.OnMatchStarted(Timestamp, localPlayerId: 1);
        session.OnEventApplied(Tag("PLAYER_TECH_LEVEL", "2"));
        session.OnEventApplied(Tag("TURN", "3"));
        var completion = session.OnMatchEnded(Timestamp.AddMinutes(10));

        var match = Assert.IsType<RecordedMatch>(completion?.Match);
        Assert.Null(completion.DiscardReason);
        Assert.Equal(4, match.Events.Count);
        Assert.Equal(RecordedEventType.MatchStarted, match.Events[0].Type);
        Assert.Equal(RecordedEventType.MatchEnded, match.Events[^1].Type);
        Assert.False(session.IsCapturing);
    }

    [Fact]
    public void SafetyRejectionDiscardsTheCaptureInsteadOfSavingIncompleteEvidence()
    {
        var session = new MatchCaptureSession();

        session.OnMatchStarted(Timestamp, localPlayerId: 1);
        session.OnEventApplied(Tag("PLAYER_TECH_LEVEL", "2"));
        session.OnEventRejected(Tag("HEALTH", "40"));
        session.OnEventApplied(Tag("TURN", "3"));
        var completion = session.OnMatchEnded(Timestamp.AddMinutes(10));

        Assert.NotNull(completion);
        Assert.Null(completion.Match);
        Assert.Contains("safety limit", completion.DiscardReason, StringComparison.Ordinal);
        Assert.False(session.IsCapturing);
        Assert.Equal(0, session.CurrentEventCount);
    }

    [Fact]
    public void RecorderEventLimitDiscardsTheCaptureWithoutThrowing()
    {
        var session = new MatchCaptureSession();
        session.OnMatchStarted(Timestamp, localPlayerId: 1);

        for (var index = 0; index < RecordingSerializer.MaximumEventCount; index++)
        {
            session.OnEventApplied(Tag("TURN", "1"));
        }

        Assert.False(session.IsCapturing);
        var completion = session.OnMatchEnded(Timestamp.AddMinutes(10));
        Assert.NotNull(completion);
        Assert.Null(completion.Match);
        Assert.Contains("Recorder limit", completion.DiscardReason, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchEndWithoutAnObservedMatchReportsNothing()
    {
        var session = new MatchCaptureSession();

        Assert.Null(session.OnMatchEnded(Timestamp));
    }

    [Fact]
    public void ExplicitDiscardDuringAnActiveMatchIsReportedAtMatchEnd()
    {
        var session = new MatchCaptureSession();
        session.OnMatchStarted(Timestamp, localPlayerId: 1);
        session.OnEventApplied(Tag("TURN", "1"));

        session.Discard("Capture was disabled by the developer.");
        session.OnEventApplied(Tag("TURN", "2"));
        var completion = session.OnMatchEnded(Timestamp.AddMinutes(5));

        Assert.NotNull(completion);
        Assert.Null(completion.Match);
        Assert.Equal("Capture was disabled by the developer.", completion.DiscardReason);
    }

    [Fact]
    public void ConsecutiveMatchesProduceIndependentRecordings()
    {
        var session = new MatchCaptureSession();

        session.OnMatchStarted(Timestamp, localPlayerId: 1);
        session.OnEventApplied(Tag("TURN", "1"));
        var first = session.OnMatchEnded(Timestamp.AddMinutes(10));

        var secondStart = Timestamp.AddMinutes(20);
        session.OnMatchStarted(secondStart, localPlayerId: 2);
        session.OnEventApplied(Tag("TURN", "1"));
        session.OnEventApplied(Tag("TURN", "2"));
        var second = session.OnMatchEnded(secondStart.AddMinutes(10));

        Assert.Equal(3, first?.Match?.Events.Count);
        Assert.Equal(4, second?.Match?.Events.Count);
        Assert.Equal(Timestamp, first!.Match!.StartedAt);
        Assert.Equal(secondStart, second!.Match!.StartedAt);
        Assert.Equal(2, second.Match.Events[0].PlayerId);
    }

    private static RawTagChanged Tag(string tag, string value) => new(
        Timestamp,
        BlockId: null,
        EntityId: 1,
        EntityName: null,
        Tag: tag,
        Value: value,
        IsCreationTag: false);
}
