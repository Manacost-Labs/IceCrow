using System.IO;
using IceCrow.App.Runtime;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Recording;

namespace IceCrow.App.Tests;

public sealed class RecordingRuntimeSoakTests
{
    private const int SimulatedMatches = 100;

    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        16,
        15,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Soak")]
    public async Task ManyConsecutiveMatchesStayBoundedThroughTheRuntime()
    {
        var statuses = new List<RecordingCaptureStatus>();
        var gate = new object();
        var persistCalls = 0;
        var runtime = new RecordingRuntime(
            "unused-local-data",
            status =>
            {
                lock (gate)
                {
                    statuses.Add(status);
                }
            },
            (_, _) =>
            {
                Interlocked.Increment(ref persistCalls);
                return Task.FromResult(new PrivateCaptureSaveResult(
                    Path.Combine("captures", $"20260816T150000Z_{Guid.NewGuid():N}.icecrow.json"),
                    []));
            });
        runtime.Start();
        runtime.SetEnabled(true);

        for (var match = 0; match < SimulatedMatches; match++)
        {
            var start = Timestamp.AddMinutes(match * 15);
            runtime.OnMatchStarted(start, localPlayerId: 1);
            for (var index = 0; index < 60; index++)
            {
                runtime.OnEventApplied(Tag());
            }

            runtime.OnMatchEnded(start.AddMinutes(10));
            await WaitForSavedCountAsync(statuses, gate, match + 1);
        }

        await runtime.DisposeAsync();

        Assert.Equal(SimulatedMatches, persistCalls);
        lock (gate)
        {
            var final = statuses[^1];
            Assert.Equal(SimulatedMatches, final.SavedCaptureCount);
            Assert.Equal(0, final.PendingSaveCount);
            Assert.Equal(RecordingSessionPhase.WaitingForNextMatch, final.SessionPhase);
            Assert.Null(final.LastError);
            Assert.Null(final.LastWarning);
        }
    }

    private static async Task WaitForSavedCountAsync(
        List<RecordingCaptureStatus> statuses,
        object gate,
        int expected)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline)
        {
            lock (gate)
            {
                if (statuses.Count > 0 && statuses[^1].SavedCaptureCount >= expected)
                {
                    return;
                }
            }

            await Task.Delay(5);
        }

        throw new TimeoutException($"Save {expected} was never reported.");
    }

    private static RawTagChanged Tag() => new(
        Timestamp,
        BlockId: null,
        EntityId: 1,
        EntityName: null,
        Tag: "TURN",
        Value: "1",
        IsCreationTag: false);
}
