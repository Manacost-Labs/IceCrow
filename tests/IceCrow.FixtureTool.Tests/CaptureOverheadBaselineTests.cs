using System.Diagnostics;
using IceCrow.Hearthstone.Logs;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Live;
using IceCrow.Recording;
using Xunit.Abstractions;

namespace IceCrow.FixtureTool.Tests;

/// <summary>
/// Repeatable developer diagnostic comparing the live pipeline cost of the
/// three observer configurations: null (Release composition), attached but
/// not capturing (Debug with capture off), and actively recording. Numbers
/// are reported for comparison on the same machine; the assertions pin only
/// structural facts, never timing.
/// </summary>
public sealed class CaptureOverheadBaselineTests(ITestOutputHelper output)
{
    private const int SimulatedTurns = 2_000;

    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        16,
        14,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Soak")]
    public void ObserverConfigurationsProcessTheSameStreamWithComparableCost()
    {
        var nullObserver = Run("null-observer", observer: null);
        var attachedIdle = Run("attached-idle", new IdleObserver());
        var recordingSession = new SessionObserver();
        var recording = Run("recording", recordingSession);

        Assert.Equal(nullObserver, attachedIdle);
        Assert.Equal(nullObserver, recording);
        Assert.False(recordingSession.Detached);
    }

    private long Run(string label, IAppliedMatchEventObserver? observer)
    {
        var coordinator = new LiveTrackingCoordinator(appliedEventObserver: observer);
        var lines = CreateLines();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        foreach (var line in lines)
        {
            _ = coordinator.Process(line);
        }

        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var applied = coordinator.Diagnostics.TrackingEventsApplied;
        var perSecond = applied / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.000_001);
        output.WriteLine(
            $"{label}: applied={applied} · events/s={perSecond:F0} · " +
            $"bytes/event={(double)allocated / Math.Max(applied, 1):F1} · " +
            $"elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F1}");

        Assert.False(coordinator.AppliedEventObserverDetached);
        return applied;
    }

    private static List<RawLogLine> CreateLines()
    {
        var payloads = new List<string>
        {
            "CREATE_GAME",
            "GameEntity EntityID=500",
            "Player EntityID=1 PlayerID=1 GameAccountId=[hi=0 lo=1]",
            "Player EntityID=2 PlayerID=2 GameAccountId=[hi=0 lo=2]",
            "TAG_CHANGE Entity=1 tag=PLAYER_TECH_LEVEL value=2",
        };
        for (var turn = 1; turn <= SimulatedTurns; turn++)
        {
            payloads.Add($"TAG_CHANGE Entity=500 tag=TURN value={turn}");
        }

        var lines = new List<RawLogLine>(payloads.Count);
        for (var index = 0; index < payloads.Count; index++)
        {
            lines.Add(new RawLogLine(
                Timestamp.AddMilliseconds(index),
                "Power",
                $"PowerTaskList.DebugPrintPower() - {payloads[index]}",
                payloads[index]));
        }

        return lines;
    }

    /// <summary>Attached observer whose session never starts — Debug capture off.</summary>
    private sealed class IdleObserver : IAppliedMatchEventObserver
    {
        private readonly MatchCaptureSession _session = new();

        public void OnMatchStarted(DateTimeOffset timestamp, int? localPlayerId)
        {
            // Capture disabled: the session is never armed.
        }

        public void OnEventApplied(GameEvent gameEvent) => _session.OnEventApplied(gameEvent);

        public void OnEventRejected(GameEvent gameEvent) => _session.OnEventRejected(gameEvent);

        public void OnMatchEnded(DateTimeOffset timestamp) => _ = _session.OnMatchEnded(timestamp);
    }

    private sealed class SessionObserver : IAppliedMatchEventObserver
    {
        private readonly MatchCaptureSession _session = new();

        public bool Detached { get; private set; }

        public void OnMatchStarted(DateTimeOffset timestamp, int? localPlayerId) =>
            _session.OnMatchStarted(timestamp, localPlayerId);

        public void OnEventApplied(GameEvent gameEvent)
        {
            _session.OnEventApplied(gameEvent);
            Detached = !_session.IsCapturing;
        }

        public void OnEventRejected(GameEvent gameEvent) =>
            _session.OnEventRejected(gameEvent);

        public void OnMatchEnded(DateTimeOffset timestamp) =>
            _ = _session.OnMatchEnded(timestamp);
    }
}
