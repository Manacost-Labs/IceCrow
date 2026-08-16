using System.Text.RegularExpressions;
using IceCrow.Recording.Tests.Fixtures;

namespace IceCrow.Recording.Tests;

public sealed class PrivateCaptureStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"icecrow-capture-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SavedCaptureRoundTripsThroughTheRecordingSerializer()
    {
        var store = new PrivateCaptureStore(_root);
        var match = DeterministicMatchFixture.Create();

        var result = await store.SaveAsync(match);

        var loaded = await RecordingSerializer.LoadAsync(result.CapturePath);
        Assert.Equal(match.Events, loaded.Events);
        Assert.Equal(match.Checkpoints, loaded.Checkpoints);
        Assert.Empty(result.PrunedCapturePaths);
    }

    [Fact]
    public async Task CaptureFileNameContainsOnlyTimestampAndRandomIdentifier()
    {
        var store = new PrivateCaptureStore(_root);

        var result = await store.SaveAsync(DeterministicMatchFixture.Create());

        Assert.Matches(
            new Regex(@"^\d{8}T\d{6}Z_[0-9a-f]{32}\.icecrow\.json$"),
            Path.GetFileName(result.CapturePath));
    }

    [Fact]
    public async Task CancelledSaveLeavesNoCaptureAndNoTemporaryFile()
    {
        var store = new PrivateCaptureStore(_root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveAsync(DeterministicMatchFixture.Create(), cancellation.Token));

        Assert.Empty(store.ListCaptures());
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task UnavailableCaptureDirectoryFailsWithoutCreatingFiles()
    {
        Directory.CreateDirectory(_root);
        var blockedRoot = Path.Combine(_root, "blocked");
        await File.WriteAllTextAsync(blockedRoot, "a file where the directory should be");
        var store = new PrivateCaptureStore(blockedRoot);

        await Assert.ThrowsAsync<IOException>(() =>
            store.SaveAsync(DeterministicMatchFixture.Create()));
    }

    [Fact]
    public async Task RetentionPrunesOldestCapturesBeyondTheMaximumCount()
    {
        var store = new PrivateCaptureStore(_root, maximumCaptureCount: 2);
        var baseline = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

        var first = await store.SaveAsync(MinimalMatch(baseline));
        var second = await store.SaveAsync(MinimalMatch(baseline.AddMinutes(10)));
        var third = await store.SaveAsync(MinimalMatch(baseline.AddMinutes(20)));

        var pruned = Assert.Single(third.PrunedCapturePaths);
        Assert.Equal(first.CapturePath, pruned);
        var survivors = store.ListCaptures().Select(static capture => capture.Path).ToArray();
        Assert.Equal([second.CapturePath, third.CapturePath], survivors);
        Assert.Empty(first.PrunedCapturePaths);
        Assert.Empty(second.PrunedCapturePaths);
    }

    [Fact]
    public async Task RetentionNeverTouchesForeignOrTemporaryFiles()
    {
        var store = new PrivateCaptureStore(_root, maximumCaptureCount: 1);
        Directory.CreateDirectory(_root);
        var foreign = Path.Combine(_root, "notes.txt");
        await File.WriteAllTextAsync(foreign, "not a capture");
        var temporary = Path.Combine(
            _root,
            $"{PrivateCaptureStore.TemporaryFilePrefix}{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(temporary, "in-flight");

        _ = await store.SaveAsync(DeterministicMatchFixture.Create());
        _ = await store.SaveAsync(DeterministicMatchFixture.Create());

        Assert.True(File.Exists(foreign));
        Assert.True(File.Exists(temporary));
        Assert.Single(store.ListCaptures());
    }

    [Fact]
    public async Task CleanupRemovesAbandonedTemporaryFilesButKeepsCaptures()
    {
        var store = new PrivateCaptureStore(_root);
        var saved = await store.SaveAsync(DeterministicMatchFixture.Create());
        var abandoned = Path.Combine(
            _root,
            $"{PrivateCaptureStore.TemporaryFilePrefix}{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(abandoned, "left behind by a crash");

        var removed = store.CleanupAbandonedTemporaryFiles();

        Assert.Equal(1, removed);
        Assert.False(File.Exists(abandoned));
        Assert.True(File.Exists(saved.CapturePath));
    }

    [Fact]
    public void TotalByteLimitBelowOneCaptureIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrivateCaptureStore(
            _root,
            maximumTotalBytes: RecordingSerializer.MaximumFileBytes - 1));
    }

    [Fact]
    public async Task RetentionFailureAfterSuccessfulSaveBecomesAMaintenanceWarning()
    {
        var store = new PrivateCaptureStore(_root, maximumCaptureCount: 1);
        var baseline = new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero);
        var first = await store.SaveAsync(MinimalMatch(baseline));

        PrivateCaptureSaveResult second;
        await using (var retentionBlock = new FileStream(
                         first.CapturePath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.None))
        {
            second = await store.SaveAsync(MinimalMatch(baseline.AddMinutes(10)));
        }

        // The new capture was persisted; the failed pruning is only maintenance.
        Assert.True(File.Exists(second.CapturePath));
        Assert.False(second.RetentionSatisfied);
        Assert.Contains("Retention pruning failed", second.MaintenanceWarning, StringComparison.Ordinal);
        Assert.Empty(second.PrunedCapturePaths);
        Assert.True(File.Exists(first.CapturePath));

        // Once the blocker is gone, the next save prunes normally again.
        var third = await store.SaveAsync(MinimalMatch(baseline.AddMinutes(20)));
        Assert.True(third.RetentionSatisfied);
        Assert.Equal(2, third.PrunedCapturePaths.Count);
        Assert.Single(store.ListCaptures());
    }

    private static RecordedMatch MinimalMatch(DateTimeOffset startedAt)
    {
        var recorder = new MatchRecorder(startedAt);
        recorder.RecordMatchStarted(startedAt, localPlayerId: 1);
        recorder.RecordMatchEnded(startedAt.AddMinutes(1));
        return recorder.CreateMatch();
    }

    [Fact]
    public void MissingStoreDirectoryListsNoCapturesAndCleansNothing()
    {
        var store = new PrivateCaptureStore(Path.Combine(_root, "never-created"));

        Assert.Empty(store.ListCaptures());
        Assert.Equal(0, store.CleanupAbandonedTemporaryFiles());
    }
}
