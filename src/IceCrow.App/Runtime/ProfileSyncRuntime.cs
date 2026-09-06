using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using IceCrow.ProfileSync;
using IceCrow.ProfileSync.Transport;

namespace IceCrow.App.Runtime;

/// <summary>
/// Composes the personal HearthPulse sync boundary: protected device
/// credential, durable outbox, HTTPS batch transport, and the single
/// background uploader. Producers hand over finished records through a
/// bounded channel; nothing here runs on the live tracking hot path.
/// </summary>
internal sealed class ProfileSyncRuntime : IAsyncDisposable
{
    private const int QueueCapacity = 64;
    private readonly ProfileOutbox _outbox;
    private readonly ProtectedProfileCredentialStore _credentials;
    private readonly HttpClient _httpClient;
    private readonly DeviceAuthorizationClient _authorization;
    private readonly ProfileSyncCoordinator _coordinator;
    private readonly Channel<ProfileEvent> _queue;
    private readonly Action<ProfileSyncStatus> _onStatusChanged;
    private long _queueOverflows;

    public ProfileSyncRuntime(
        string localDataDirectory,
        Uri hearthPulseOrigin,
        Action<ProfileSyncStatus> onStatusChanged,
        string clientVersion)
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
        _queue = Channel.CreateBounded<ProfileEvent>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
            AllowSynchronousContinuations = false,
        });
    }

    public ProfileSyncStatus Status => _coordinator.Status;

    public DeviceAuthorizationClient Authorization => _authorization;

    public IProfileCredentialStore Credentials => _credentials;

    /// <summary>Queues a finished record for the outbox; never blocks the caller.</summary>
    public bool TryQueue(ProfileEvent profileEvent)
    {
        ArgumentNullException.ThrowIfNull(profileEvent);
        if (_queue.Writer.TryWrite(profileEvent))
        {
            return true;
        }

        Interlocked.Increment(ref _queueOverflows);
        _coordinator.ReportOutboxOverflow();
        return false;
    }

    public void SetGameplayActive(bool active) => _coordinator.SetGameplayActive(active);

    public void Complete() => _queue.Writer.TryComplete();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var uploader = _coordinator.RunAsync(cancellationToken);
        try
        {
            await foreach (var profileEvent in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await PersistAsync(profileEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        await uploader.ConfigureAwait(false);
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

    public ValueTask DisposeAsync()
    {
        _coordinator.StatusChanged -= _onStatusChanged;
        _coordinator.Dispose();
        _outbox.Dispose();
        _credentials.Dispose();
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
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

    private async Task PersistAsync(ProfileEvent profileEvent, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _outbox.EnqueueAsync(profileEvent, cancellationToken).ConfigureAwait(false);
            if (result == ProfileOutboxResult.Full)
            {
                _coordinator.ReportOutboxOverflow();
                return;
            }

            _coordinator.Notify();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Debug.WriteLine($"Profile outbox unavailable: {exception.GetType().Name}");
        }
    }
}
