using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using IceCrow.App.History;
using IceCrow.ProfileSync;
using IceCrow.ProfileSync.History;

namespace IceCrow.App;

public partial class HistoryWindow : Window
{
    private readonly ObservableCollection<MatchHistoryRow> _matches = [];
    private readonly ObservableCollection<MatchHistoryRow> _recent = [];
    private readonly ObservableCollection<DeckHistoryRow> _decks = [];
    private IReadOnlyList<MatchHistoryRow> _allMatches = [];

    public HistoryWindow()
    {
        InitializeComponent();
        MatchList.ItemsSource = _matches;
        RecentMatches.ItemsSource = _recent;
        DeckList.ItemsSource = _decks;
        UpdateEmptyStates();
    }

    public void ApplySnapshot(ProfileHistorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Dispatcher.VerifyAccess();
        _allMatches = snapshot.Matches.Select(MatchHistoryRow.From).ToArray();

        _recent.Clear();
        foreach (var row in _allMatches.Take(8))
        {
            _recent.Add(row);
        }

        _decks.Clear();
        foreach (var deck in snapshot.Decks.Select(DeckHistoryRow.From))
        {
            _decks.Add(deck);
        }

        TotalMatches.Text = snapshot.Matches.Length.ToString(CultureInfo.CurrentCulture);
        TotalWins.Text = snapshot.Wins.ToString(CultureInfo.CurrentCulture);
        TotalLosses.Text = snapshot.Losses.ToString(CultureInfo.CurrentCulture);
        TotalBattlegrounds.Text = snapshot.BattlegroundsGames.ToString(CultureInfo.CurrentCulture);
        ArchiveStatus.Text = snapshot.RecoveredTruncatedTail
            ? $"Сохранено матчей: {snapshot.Matches.Length} · восстановлен незавершённый хвост файла"
            : $"Сохранено матчей: {snapshot.Matches.Length}";
        ApplyFilter();
    }

    public void SetSyncStatus(ProfileSyncStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        Dispatcher.VerifyAccess();
        SyncStatus.Text = status.Phase switch
        {
            ProfileSyncPhase.NotLinked => "HearthPulse: аккаунт не подключён",
            ProfileSyncPhase.Uploading => $"HearthPulse: отправка · в очереди {status.PendingEvents}",
            ProfileSyncPhase.Idle => $"HearthPulse: синхронизировано · в очереди {status.PendingEvents}",
            ProfileSyncPhase.BackingOff => $"HearthPulse: повторная попытка · {status.PendingEvents}",
            ProfileSyncPhase.AuthorizationRequired => "HearthPulse: требуется повторное подключение",
            _ => $"HearthPulse: {status.Phase}",
        };
    }

    public void SetRuntimeStatus(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        Dispatcher.VerifyAccess();
        TrackingStatus.Text = status;
    }

    private void ApplyFilter()
    {
        var requestedMode = (ModeFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        var query = SearchBox.Text.Trim();
        var filtered = _allMatches.Where(row =>
            ModeMatches(row.Match.Mode, requestedMode) &&
            (query.Length == 0 || row.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase)));

        var selectedId = (MatchList.SelectedItem as MatchHistoryRow)?.Match.EventId;
        _matches.Clear();
        foreach (var row in filtered)
        {
            _matches.Add(row);
        }

        MatchList.SelectedItem = selectedId is Guid id
            ? _matches.FirstOrDefault(row => row.Match.EventId == id)
            : _matches.FirstOrDefault();
        UpdateEmptyStates();
    }

    private void UpdateEmptyStates()
    {
        NoMatches.Visibility = _matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoDecks.Visibility = _decks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool ModeMatches(HistoryGameMode mode, string requested) => requested switch
    {
        "standard" => mode == HistoryGameMode.Standard,
        "wild" => mode == HistoryGameMode.Wild,
        "arena" => mode == HistoryGameMode.Arena,
        "battlegrounds" => mode == HistoryGameMode.Battlegrounds,
        _ => true,
    };

    private void OnFilterChanged(object sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (IsInitialized)
        {
            ApplyFilter();
        }
    }

    private void OnMatchSelected(object sender, SelectionChangedEventArgs eventArgs)
    {
        _ = sender;
        eventArgs.Handled = true;
        if (MatchList.SelectedItem is not MatchHistoryRow row)
        {
            DetailOutcome.Text = "Выберите матч";
            DetailMode.Text = "—";
            DetailTime.Text = "—";
            DetailDuration.Text = "—";
            DetailPlayerHero.Text = "—";
            DetailOpponentHero.Text = "—";
            DetailConfidence.Text = "—";
            return;
        }

        DetailOutcome.Text = row.Outcome;
        DetailMode.Text = row.Mode;
        DetailTime.Text = row.PlayedAt;
        DetailDuration.Text = $"{row.Duration} · {row.Match.Turns} ходов";
        DetailPlayerHero.Text = row.PlayerHero;
        DetailOpponentHero.Text = row.OpponentHero;
        DetailConfidence.Text = row.Confidence;
    }
}
