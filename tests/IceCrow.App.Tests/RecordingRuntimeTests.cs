using System.IO;
using IceCrow.App.Runtime;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Recording;

namespace IceCrow.App.Tests;

public sealed class RecordingRuntimeTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        16,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task EnableMovesSessionFromOffToWaiting()
    {
        var sink = new StatusSink();
        await using var runtime = Create(sink, ImmediatePersist());
        runtime.Start();

        runtime.SetEnabled(true);

        var status = sink.Latest;
        Assert.True(status.IsEnabled);
        Assert.Equal(RecordingSessionPhase.WaitingForNextMatch, status.SessionPhase);
        Assert.Equal(RecordingPersistencePhase.Idle, status.PersistencePhase);
        Assert.Null(status.LastError);
    }

    [Fact]
    public async Task MatchStartRecordsAndEventCountIsPublishedOnStride()
    {
        var sink = new StatusSink();
        await using var runtime = Create(sink, ImmediatePersist());
        runtime.Start();
        runtime.SetEnabled(true);

        runtime.OnMatchStarted(Timestamp, localPlayerId: 1);
        Assert.Equal(RecordingSessionPhase.Recording, sink.Latest.SessionPhase);

        for (var index = 0; index < 120; index++)
        {
            runtime.OnEventApplied(Tag());
        }

        var strided = await sink.WaitForAsync(static status =>
            status.SessionPhase == RecordingSessionPhase.Recording &&
            status.CurrentEventCount >= 100);
        Assert.True(strided.CurrentEventCount >= 100);
    }

    [Fact]
    public async Task CompletedMatchSavesWhileSessionReturnsToWaiting()
    {
        var sink = new StatusSink();
        var save = new TaskCompletionSource<PrivateCaptureSaveResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var runtime = Create(sink, (_, _) => save.Task);
        runtime.Start();
        runtime.SetEnabled(true);

        RunMatch(runtime);

        var saving = await sink.WaitForAsync(static status =>
            status.PersistencePhase == RecordingPersistencePhase.Saving);
        Assert.Equal(RecordingSessionPhase.WaitingForNextMatch, saving.SessionPhase);
        Assert.Equal(1, saving.PendingSaveCount);

        save.SetResult(Result("20260816T120000Z_saved.icecrow.json"));
        var saved = await sink.WaitForAsync(static status =>
            status.PersistencePhase == RecordingPersistencePhase.Saved);
        Assert.Equal(1, saved.SavedCaptureCount);
        Assert.Equal(0, saved.PendingSaveCount);
        Assert.Equal("20260816T120000Z_saved.icecrow.json", saved.LastSavedFileName);
    }

    [Fact]
    public async Task SaveOfPreviousMatchNeverOverwritesRecordingSession()
    {
        var sink = new StatusSink();
        var save = new TaskCompletionSource<PrivateCaptureSaveResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var runtime = Create(sink, (_, _) => save.Task);
        runtime.Start();
        runtime.SetEnabled(true);

        RunMatch(runtime);
        runtime.OnMatchStarted(Timestamp.AddMinutes(5), localPlayerId: 1);

        var overlapping = await sink.WaitForAsync(static status =>
            status.SessionPhase == RecordingSessionPhase.Recording &&
            status.PersistencePhase == RecordingPersistencePhase.Saving);
        Assert.Equal(1, overlapping.PendingSaveCount);

        save.SetResult(Result());
        var saved = await sink.WaitForAsync(static status =>
            status.PersistencePhase == RecordingPersistencePhase.Saved);
        Assert.Equal(RecordingSessionPhase.Recording, saved.SessionPhase);
    }

    [Fact]
    public async Task FailedSaveOfPreviousMatchKeepsRecordingSessionAndSavedCount()
    {
        var sink = new StatusSink();
        var save = new TaskCompletionSource<PrivateCaptureSaveResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var runtime = Create(sink, (_, _) => save.Task);
        runtime.Start();
        runtime.SetEnabled(true);

        RunMatch(runtime);
        runtime.OnMatchStarted(Timestamp.AddMinutes(5), localPlayerId: 1);
        save.SetException(new IOException("disk detached"));

        var failed = await sink.WaitForAsync(static status =>
            status.PersistencePhase == RecordingPersistencePhase.Failed);
        Assert.Equal(RecordingSessionPhase.Recording, failed.SessionPhase);
        Assert.Contains("Capture save failed", failed.LastError, StringComparison.Ordinal);
        Assert.Equal(0, failed.SavedCaptureCount);
        Assert.Equal(0, failed.PendingSaveCount);
    }

    [Fact]
    public async Task RetentionWarningIsReportedAsWarningNotFailure()
    {
        var sink = new StatusSink();
        await using var runtime = Create(
            sink,
            (_, _) => Task.FromResult(new PrivateCaptureSaveResult(
                Path.Combine("captures", "20260816T120000Z_w.icecrow.json"),
                [],
                "Retention pruning failed; older captures may remain: locked")));
        runtime.Start();
        runtime.SetEnabled(true);

        RunMatch(runtime);

        var warned = await sink.WaitForAsync(static status =>
            status.PersistencePhase == RecordingPersistencePhase.Warning);
        Assert.Equal(1, warned.SavedCaptureCount);
        Assert.Contains("Retention pruning failed", warned.LastWarning, StringComparison.Ordinal);
        Assert.Null(warned.LastError);
    }

    [Fact]
    public async Task IntentionalDisableDuringRecordingIsANoticeNotAFailure()
    {
        var sink = new StatusSink();
        var persistCalls = 0;
        await using var runtime = Create(sink, CountingPersist(() => persistCalls++));
        runtime.Start();
        runtime.SetEnabled(true);
        runtime.OnMatchStarted(Timestamp, localPlayerId: 1);
        runtime.OnEventApplied(Tag());

        runtime.SetEnabled(false);
        var disabled = sink.Latest;
        Assert.Equal(RecordingSessionPhase.Off, disabled.SessionPhase);
        Assert.NotNull(disabled.LastNotice);
        Assert.Null(disabled.LastError);

        runtime.OnMatchEnded(Timestamp.AddMinutes(1));
        var afterEnd = sink.Latest;
        Assert.Equal(RecordingSessionPhase.Off, afterEnd.SessionPhase);
        Assert.Equal(RecordingPersistencePhase.Idle, afterEnd.PersistencePhase);
        Assert.Null(afterEnd.LastError);
        Assert.Equal(0, persistCalls);
    }

    [Fact]
    public async Task DisableAndReEnableBeforeMatchEndRemainsWaiting()
    {
        var sink = new StatusSink();
        var persistCalls = 0;
        await using var runtime = Create(sink, CountingPersist(() => persistCalls++));
        runtime.Start();
        runtime.SetEnabled(true);
        runtime.OnMatchStarted(Timestamp, localPlayerId: 1);

        runtime.SetEnabled(false);
        runtime.SetEnabled(true);
        Assert.Equal(RecordingSessionPhase.WaitingForNextMatch, sink.Latest.SessionPhase);

        runtime.OnMatchEnded(Timestamp.AddMinutes(1));
        var afterEnd = sink.Latest;
        Assert.Equal(RecordingSessionPhase.WaitingForNextMatch, afterEnd.SessionPhase);
        Assert.Null(afterEnd.LastError);
        Assert.Equal(0, persistCalls);

        runtime.OnMatchStarted(Timestamp.AddMinutes(2), localPlayerId: 1);
        Assert.Equal(RecordingSessionPhase.Recording, sink.Latest.SessionPhase);
    }

    [Fact]
    public async Task EnableDuringAnActiveMatchWaitsForTheNextMatch()
    {
        var sink = new StatusSink();
        var persistCalls = 0;
        await using var runtime = Create(sink, CountingPersist(() => persistCalls++));
        runtime.Start();

        // The authoritative MatchStarted already passed while capture was off.
        runtime.OnMatchStarted(Timestamp, localPlayerId: 1);
        runtime.SetEnabled(true);
        Assert.Equal(RecordingSessionPhase.WaitingForNextMatch, sink.Latest.SessionPhase);

        runtime.OnEventApplied(Tag());
        Assert.Equal(0, sink.Latest.CurrentEventCount);

        runtime.OnMatchEnded(Timestamp.AddMinutes(1));
        Assert.Null(sink.Latest.LastError);
        Assert.Equal(0, persistCalls);

        runtime.OnMatchStarted(Timestamp.AddMinutes(2), localPlayerId: 1);
        Assert.Equal(RecordingSessionPhase.Recording, sink.Latest.SessionPhase);
    }

    [Fact]
    public async Task SafetyRejectionDiscardsTheCaptureAndRecoversNextMatch()
    {
        var sink = new StatusSink();
        var persistCalls = 0;
        await using var runtime = Create(sink, CountingPersist(() => persistCalls++));
        runtime.Start();
        runtime.SetEnabled(true);
        runtime.OnMatchStarted(Timestamp, localPlayerId: 1);
        runtime.OnEventApplied(Tag());

        runtime.OnEventRejected(Tag());
        var rejected = sink.Latest;
        Assert.Equal(RecordingSessionPhase.WaitingForNextMatch, rejected.SessionPhase);
        Assert.Contains("safety rejection", rejected.LastError, StringComparison.Ordinal);

        runtime.OnMatchEnded(Timestamp.AddMinutes(1));
        Assert.Equal(0, persistCalls);

        runtime.OnMatchStarted(Timestamp.AddMinutes(2), localPlayerId: 1);
        Assert.Equal(RecordingSessionPhase.Recording, sink.Latest.SessionPhase);
    }

    [Fact]
    public async Task RecorderLimitDiscardsTheCaptureWithoutSaving()
    {
        var sink = new StatusSink();
        var persistCalls = 0;
        await using var runtime = Create(sink, CountingPersist(() => persistCalls++));
        runtime.Start();
        runtime.SetEnabled(true);
        runtime.OnMatchStarted(Timestamp, localPlayerId: 1);

        for (var index = 0; index < RecordingSerializer.MaximumEventCount; index++)
        {
            runtime.OnEventApplied(Tag());
        }

        var discarded = sink.Latest;
        Assert.Equal(RecordingSessionPhase.WaitingForNextMatch, discarded.SessionPhase);
        Assert.Contains("discarded mid-match", discarded.LastError, StringComparison.Ordinal);

        runtime.OnMatchEnded(Timestamp.AddMinutes(1));
        Assert.Contains("Recorder limit", sink.Latest.LastError, StringComparison.Ordinal);
        Assert.Equal(0, persistCalls);
    }

    [Fact]
    public async Task FullPersistenceQueueDropsTheCaptureWithAnExplicitError()
    {
        var sink = new StatusSink();
        var firstSaveStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSaves = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var runtime = Create(sink, async (_, _) =>
        {
            firstSaveStarted.TrySetResult();
            await releaseSaves.Task;
            return Result();
        });
        runtime.Start();
        runtime.SetEnabled(true);

        RunMatch(runtime, offsetMinutes: 0);
        await firstSaveStarted.Task;

        // The worker holds match one; two more fill the bounded queue.
        RunMatch(runtime, offsetMinutes: 10);
        RunMatch(runtime, offsetMinutes: 20);
        RunMatch(runtime, offsetMinutes: 30);

        var overflow = await sink.WaitForAsync(static status =>
            status.LastError is not null &&
            status.LastError.Contains("queue is full", StringComparison.Ordinal));
        Assert.Equal(3, overflow.PendingSaveCount);

        releaseSaves.SetResult();
        var drained = await sink.WaitForAsync(static status =>
            status.SavedCaptureCount == 3);
        Assert.Equal(0, drained.PendingSaveCount);
    }

    [Fact]
    public async Task ShutdownDiscardsTheActiveCaptureWithoutSaving()
    {
        var sink = new StatusSink();
        var persistCalls = 0;
        var runtime = Create(sink, CountingPersist(() => persistCalls++));
        runtime.Start();
        runtime.SetEnabled(true);
        runtime.OnMatchStarted(Timestamp, localPlayerId: 1);
        runtime.OnEventApplied(Tag());

        await runtime.DisposeAsync();

        Assert.Equal(0, persistCalls);
    }

    [Fact]
    public async Task ShutdownDrainsAPendingSaveWithinTheGracePeriod()
    {
        var sink = new StatusSink();
        var runtime = Create(sink, async (_, cancellationToken) =>
        {
            await Task.Delay(100, cancellationToken);
            return Result();
        });
        runtime.Start();
        runtime.SetEnabled(true);
        RunMatch(runtime);

        await runtime.DisposeAsync();

        var saved = await sink.WaitForAsync(static status =>
            status.SavedCaptureCount == 1);
        Assert.Equal(RecordingPersistencePhase.Saved, saved.PersistencePhase);
    }

    [Fact]
    public async Task ShutdownCancelsASaveThatObservesCancellation()
    {
        var sink = new StatusSink();
        var saveStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = Create(
            sink,
            async (_, cancellationToken) =>
            {
                saveStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Result();
            },
            shutdownGracePeriod: TimeSpan.FromMilliseconds(100));
        runtime.Start();
        runtime.SetEnabled(true);
        RunMatch(runtime);
        await saveStarted.Task;

        await runtime.DisposeAsync();

        var cancelled = await sink.WaitForAsync(static status =>
            status.LastWarning is not null &&
            status.LastWarning.Contains("cancelled during shutdown", StringComparison.Ordinal));
        Assert.Equal(0, cancelled.SavedCaptureCount);
    }

    [Fact]
    public async Task ShutdownAbandonsASaveThatIgnoresCancellation()
    {
        var sink = new StatusSink();
        var neverObservesCancellation = new TaskCompletionSource<PrivateCaptureSaveResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = Create(
            sink,
            (_, _) => neverObservesCancellation.Task,
            shutdownGracePeriod: TimeSpan.FromMilliseconds(100));
        runtime.Start();
        runtime.SetEnabled(true);
        RunMatch(runtime);

        var dispose = runtime.DisposeAsync().AsTask();
        var finished = await Task.WhenAny(dispose, Task.Delay(5000));

        Assert.Same(dispose, finished);
        neverObservesCancellation.SetResult(Result());
    }

    [Fact]
    public async Task ThrowingStatusCallbackNeverBreaksTheCapturePipeline()
    {
        var persistCalls = 0;
        var runtime = new RecordingRuntime(
            "unused-local-data",
            _ => throw new InvalidOperationException("status consumer failure"),
            CountingPersist(() => persistCalls++));
        runtime.Start();
        runtime.SetEnabled(true);
        RunMatch(runtime);

        var deadline = Environment.TickCount64 + 5000;
        while (Volatile.Read(ref persistCalls) == 0 && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, persistCalls);
        await runtime.DisposeAsync();
    }

    private static RecordingRuntime Create(
        StatusSink sink,
        Func<RecordedMatch, CancellationToken, Task<PrivateCaptureSaveResult>> persist,
        TimeSpan? shutdownGracePeriod = null) =>
        new("unused-local-data", sink.Add, persist, shutdownGracePeriod);

    private static Func<RecordedMatch, CancellationToken, Task<PrivateCaptureSaveResult>>
        ImmediatePersist() => static (_, _) => Task.FromResult(Result());

    private static Func<RecordedMatch, CancellationToken, Task<PrivateCaptureSaveResult>>
        CountingPersist(Action onCalled) => (_, _) =>
        {
            onCalled();
            return Task.FromResult(Result());
        };

    private static PrivateCaptureSaveResult Result(
        string fileName = "20260816T120000Z_result.icecrow.json") =>
        new(Path.Combine("captures", fileName), []);

    private static void RunMatch(RecordingRuntime runtime, int offsetMinutes = 0)
    {
        var start = Timestamp.AddMinutes(offsetMinutes);
        runtime.OnMatchStarted(start, localPlayerId: 1);
        runtime.OnEventApplied(Tag());
        runtime.OnEventApplied(Tag());
        runtime.OnMatchEnded(start.AddMinutes(1));
    }

    private static RawTagChanged Tag() => new(
        Timestamp,
        BlockId: null,
        EntityId: 1,
        EntityName: null,
        Tag: "TURN",
        Value: "1",
        IsCreationTag: false);

    private sealed class StatusSink
    {
        private readonly object _lock = new();
        private readonly List<RecordingCaptureStatus> _statuses = [];

        public RecordingCaptureStatus Latest
        {
            get
            {
                lock (_lock)
                {
                    Assert.NotEmpty(_statuses);
                    return _statuses[^1];
                }
            }
        }

        public void Add(RecordingCaptureStatus status)
        {
            lock (_lock)
            {
                _statuses.Add(status);
            }
        }

        public async Task<RecordingCaptureStatus> WaitForAsync(
            Func<RecordingCaptureStatus, bool> predicate,
            int timeoutMilliseconds = 5000)
        {
            var deadline = Environment.TickCount64 + timeoutMilliseconds;
            while (Environment.TickCount64 < deadline)
            {
                lock (_lock)
                {
                    for (var index = _statuses.Count - 1; index >= 0; index--)
                    {
                        if (predicate(_statuses[index]))
                        {
                            return _statuses[index];
                        }
                    }
                }

                await Task.Delay(10);
            }

            throw new TimeoutException("The expected capture status was never published.");
        }
    }
}
