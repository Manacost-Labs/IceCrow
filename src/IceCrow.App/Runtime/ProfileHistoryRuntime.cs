using System.IO;
using IceCrow.ProfileSync;
using IceCrow.ProfileSync.History;

namespace IceCrow.App.Runtime;

/// <summary>Owns the always-on local match archive independently from server sync.</summary>
internal sealed class ProfileHistoryRuntime : IDisposable
{
    private readonly ProfileHistoryStore _store;
    private readonly ProfileHistoryWorker _worker;
    private readonly Action<ProfileHistorySnapshot> _onSnapshotChanged;
    private readonly Action<string> _onPersistenceUnavailable;
    private string? _lastReportedFailure;

    public ProfileHistoryRuntime(
        string localDataDirectory,
        Action<ProfileHistorySnapshot> onSnapshotChanged,
        Action<string> onPersistenceUnavailable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localDataDirectory);
        ArgumentNullException.ThrowIfNull(onSnapshotChanged);
        ArgumentNullException.ThrowIfNull(onPersistenceUnavailable);
        _onSnapshotChanged = onSnapshotChanged;
        _onPersistenceUnavailable = onPersistenceUnavailable;
        _store = new ProfileHistoryStore(Path.Combine(localDataDirectory, "history", "matches.jsonl"));
        _worker = new ProfileHistoryWorker(_store);
        _worker.SnapshotChanged += PublishSnapshot;
        _worker.PersistenceUnavailable += ReportPersistenceUnavailable;
    }

    public ProfileHandoffResult TryQueue(ProfileEvent profileEvent) => _worker.Accept(profileEvent);

    public Task RunAsync(CancellationToken cancellationToken) => _worker.RunAsync(cancellationToken);

    public void Complete() => _worker.Complete();

    public void Dispose()
    {
        _worker.SnapshotChanged -= PublishSnapshot;
        _worker.PersistenceUnavailable -= ReportPersistenceUnavailable;
        _worker.Dispose();
        _store.Dispose();
    }

    private void ReportPersistenceUnavailable(string failure)
    {
        var previous = Interlocked.Exchange(ref _lastReportedFailure, failure);
        if (!string.Equals(previous, failure, StringComparison.Ordinal))
        {
            _onPersistenceUnavailable(failure);
        }
    }

    private void PublishSnapshot(ProfileHistorySnapshot snapshot)
    {
        Interlocked.Exchange(ref _lastReportedFailure, null);
        _onSnapshotChanged(snapshot);
    }
}
