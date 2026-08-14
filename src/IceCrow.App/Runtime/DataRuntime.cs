using System.Net.Http;
using System.IO;
using IceCrow.Hearthstone.Data;
using IceCrow.Infrastructure.ManacostApi;

namespace IceCrow.App.Runtime;

internal sealed class DataRuntime : IAsyncDisposable
{
    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
    });
    private readonly ManacostDataSynchronizer _synchronizer;
    private readonly Action<ManacostDataStatus> _onStatusChanged;

    public DataRuntime(string localDataDirectory, Action<ManacostDataStatus> onStatusChanged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localDataDirectory);
        ArgumentNullException.ThrowIfNull(onStatusChanged);
        _onStatusChanged = onStatusChanged;
        Database = new InMemoryCardDatabase();
        var store = new JsonHearthstoneDataStore(
            Path.Combine(localDataDirectory, "data", "hearthstone-data.json"));
        var client = new ManacostDatasetClient(_httpClient);
        _synchronizer = new ManacostDataSynchronizer(Database, store, client);
        _synchronizer.StatusChanged += OnStatusChanged;
    }

    public InMemoryCardDatabase Database { get; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _synchronizer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _synchronizer.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _synchronizer.StatusChanged -= OnStatusChanged;
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnStatusChanged(ManacostDataStatus status) => _onStatusChanged(status);
}
