using IceCrow.Hearthstone.Data;

namespace IceCrow.Infrastructure.ManacostApi;

public sealed class ManacostDataSynchronizer
{
    private readonly InMemoryCardDatabase _database;
    private readonly IHearthstoneDataStore _store;
    private readonly ManacostDatasetClient _client;

    public ManacostDataSynchronizer(
        InMemoryCardDatabase database,
        IHearthstoneDataStore store,
        ManacostDatasetClient client)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(client);
        _database = database;
        _store = store;
        _client = client;
    }

    public event Action<ManacostDataStatus>? StatusChanged;

    public ManacostDataStatus Status { get; private set; } = new(
        false, true, null, null, null, 0, 0, 0, null);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var cached = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                _database.Replace(cached);
                PublishStatus(offline: true, lastSync: null, error: null);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            PublishStatus(offline: true, lastSync: null, error: exception.Message);
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var downloaded = await _client.DownloadAsync(cancellationToken).ConfigureAwait(false);
            if (string.Equals(
                    downloaded.Version.Sha256,
                    _database.Version?.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                PublishStatus(offline: false, lastSync: DateTimeOffset.UtcNow, error: null);
                return;
            }

            await _store.SaveAsync(downloaded, cancellationToken).ConfigureAwait(false);
            _database.Replace(downloaded);
            PublishStatus(offline: false, lastSync: DateTimeOffset.UtcNow, error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or ManacostApiException or
                IOException or UnauthorizedAccessException or InvalidDataException)
        {
            PublishStatus(offline: true, lastSync: Status.LastSync, error: exception.Message);
        }
    }

    private void PublishStatus(bool offline, DateTimeOffset? lastSync, string? error)
    {
        Status = new ManacostDataStatus(
            _database.Version is not null,
            offline,
            _database.Version?.DataVersion,
            _database.Version?.HearthstoneBuild,
            lastSync,
            _database.CardCount,
            _database.QueryBattlegroundsCards(new CardQuery()).Count(card =>
                string.Equals(card.CardType, "minion", StringComparison.OrdinalIgnoreCase)),
            _database.HeroCount,
            error);
        StatusChanged?.Invoke(Status);
    }
}
