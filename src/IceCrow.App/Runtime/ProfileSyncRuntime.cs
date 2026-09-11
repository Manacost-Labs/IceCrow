using System.Diagnostics;
using System.IO;
using System.Net.Http;
using IceCrow.ProfileSync;
using IceCrow.ProfileSync.Collection;
using IceCrow.ProfileSync.Transport;

namespace IceCrow.App.Runtime;

/// <summary>
/// Composes the personal HearthPulse sync boundary: protected device
/// credential, durable journal outbox, the single persistence worker that
/// owns every accepted record until it is committed, the HTTPS batch
/// transport, and the single background uploader. Nothing here runs on the
/// live tracking hot path beyond a non-blocking handoff.
/// </summary>
internal sealed class ProfileSyncRuntime : IAsyncDisposable
{
    private readonly ProfileOutbox _outbox;
    private readonly ProtectedProfileCredentialStore _credentials;
    private readonly HttpClient _httpClient;
    private readonly DeviceAuthorizationClient _authorization;
    private readonly ProfileSyncCoordinator _coordinator;
    private readonly ProfilePersistenceWorker _persistence;
    private readonly HdtCollectionExportSource _collectionSource;
    private readonly CollectionSyncCoordinator _collection;
    private readonly SemaphoreSlim _collectionCommandGate = new(1, 1);
    private readonly Action<ProfileSyncStatus> _onStatusChanged;
    private Task _persistenceTask = Task.CompletedTask;

    public ProfileSyncRuntime(
        string localDataDirectory,
        Uri hearthPulseOrigin,
        Action<ProfileSyncStatus> onStatusChanged,
        string clientVersion,
        HdtCollectionExportLocator? collectionLocator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localDataDirectory);
        ArgumentNullException.ThrowIfNull(hearthPulseOrigin);
        ArgumentNullException.ThrowIfNull(onStatusChanged);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientVersion);
        _onStatusChanged = onStatusChanged;
        var directory = Path.Combine(localDataDirectory, "profile");
        _outbox = new ProfileOutbox(Path.Combine(directory, "outbox.json"));
        _credentials = new ProtectedProfileCredentialStore(
            Path.Combine(directory, "credential.bin"),
            new DataProtectionSecretProtector());
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = HttpProfileSyncTransport.MaximumResponseBytes,
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"IceCrow/{clientVersion}");
        _authorization = new DeviceAuthorizationClient(_httpClient, hearthPulseOrigin);
        var transport = new HttpProfileSyncTransport(_httpClient, _credentials, _authorization);
        _coordinator = new ProfileSyncCoordinator(_outbox, transport, IsLinkedAsync);
        _coordinator.StatusChanged += _onStatusChanged;
        _persistence = new ProfilePersistenceWorker(_outbox);
        _persistence.Persisted += OnPersisted;
        _persistence.StatusChanged += OnHandoffStatusChanged;
        _collectionSource = new HdtCollectionExportSource(
            collectionLocator ?? HdtCollectionExportLocator.CreateDefault());
        _collection = new CollectionSyncCoordinator(
            _collectionSource,
            _outbox,
            Path.Combine(directory, "collection-sync.json"));
    }

    public ProfileSyncStatus Status => _coordinator.Status;

    public ProfileHandoffStatus HandoffStatus => _persistence.Status;

    public CollectionSyncStatus CollectionStatus => _collection.Status;

    public DeviceAuthorizationClient Authorization => _authorization;

    public IProfileCredentialStore Credentials => _credentials;

    /// <summary>
    /// Hands a finished record to the persistence worker without blocking.
    /// False means the bounded handoff refused it explicitly; nothing is ever
    /// dropped silently.
    /// </summary>
    public bool TryQueue(ProfileEvent profileEvent)
    {
        ArgumentNullException.ThrowIfNull(profileEvent);
        return _persistence.Accept(profileEvent) == ProfileHandoffResult.Accepted;
    }

    public void SetGameplayActive(bool active) => _coordinator.SetGameplayActive(active);

    /// <summary>Stops accepting records; accepted ones still drain to the outbox.</summary>
    public void Complete() => _persistence.Complete();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // The persistence worker only stops accepting on cancellation; it
        // keeps draining accepted records within its grace period so the
        // uploader is stopped after, never before, the durable commit.
        _persistenceTask = _persistence.RunAsync(cancellationToken);
        try
        {
            try
            {
                _ = await RefreshCollectionAsync(CollectionRefreshTrigger.Startup, null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Debug.WriteLine($"Collection startup import unavailable: {exception.GetType().Name}");
            }

            await _coordinator.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _persistenceTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Imports one explicitly selected complete HDT exporter snapshot, or
    /// refreshes from the newest auto-discovered snapshot when path is null.
    /// </summary>
    public async Task<CollectionSyncOutcome> RefreshCollectionAsync(
        CollectionRefreshTrigger trigger,
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        await _collectionCommandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (path is not null)
            {
                _collectionSource.UseExportFile(path);
            }

            var outcome = await _collection.RefreshAsync(trigger, cancellationToken).ConfigureAwait(false);
            if (outcome is CollectionSyncOutcome.Enqueued or CollectionSyncOutcome.Replaced)
            {
                _coordinator.Notify();
            }

            return outcome;
        }
        finally
        {
            _collectionCommandGate.Release();
        }
    }

    /// <summary>Stores a freshly linked credential and resumes uploads.</summary>
    public async Task SaveCredentialAsync(ProfileCredential credential, CancellationToken cancellationToken)
    {
        await _credentials.SaveAsync(credential, cancellationToken).ConfigureAwait(false);
        _coordinator.ResetAuthorization();
    }

    public async Task UnlinkAsync(CancellationToken cancellationToken)
    {
        var credential = await LoadCredentialSafelyAsync(cancellationToken).ConfigureAwait(false);
        if (credential is not null)
        {
            _ = await _authorization.RevokeAsync(credential, cancellationToken).ConfigureAwait(false);
        }

        await _credentials.ClearAsync(cancellationToken).ConfigureAwait(false);
        _coordinator.Notify();
    }

    public async ValueTask DisposeAsync()
    {
        _persistence.Complete();
        try
        {
            await _persistenceTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _persistence.Persisted -= OnPersisted;
        _persistence.StatusChanged -= OnHandoffStatusChanged;
        _coordinator.StatusChanged -= _onStatusChanged;
        _collection.Dispose();
        _collectionCommandGate.Dispose();
        _persistence.Dispose();
        _coordinator.Dispose();
        _outbox.Dispose();
        _credentials.Dispose();
        _httpClient.Dispose();
    }

    private async ValueTask<bool> IsLinkedAsync(CancellationToken cancellationToken) =>
        await LoadCredentialSafelyAsync(cancellationToken).ConfigureAwait(false) is not null;

    private async Task<ProfileCredential?> LoadCredentialSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _credentials.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Debug.WriteLine($"Profile credential unavailable: {exception.GetType().Name}");
            return null;
        }
    }

    private void OnPersisted(ProfileEvent profileEvent) => _coordinator.Notify();

    private void OnHandoffStatusChanged(ProfileHandoffStatus status)
    {
        if (status.Phase == ProfileHandoffPhase.Full)
        {
            _coordinator.ReportOutboxOverflow();
        }
    }
}
