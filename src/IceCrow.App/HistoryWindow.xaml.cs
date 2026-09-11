using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private ProfileSyncStatus _syncStatus = ProfileSyncStatus.Initial;
    private ProfileLinkUpdate? _linkUpdate;
    private Uri? _verificationUri;
    private HistoryNavigationRoute _navigation = HistoryNavigation.Resolve("overview");

    public event Action? LinkRequested;

    public event Action? UnlinkRequested;

    public event Action? CancelLinkRequested;

    public event Action<Uri>? VerificationPageRequested;

    public HistoryWindow()
    {
        InitializeComponent();
        MatchList.ItemsSource = _matches;
        RecentMatches.ItemsSource = _recent;
        DeckList.ItemsSource = _decks;
        UpdateEmptyStates();
        RenderAccountState();
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
        _syncStatus = status;
        SyncStatus.Text = status.Phase switch
        {
            ProfileSyncPhase.NotLinked => "HearthPulse: аккаунт не подключён",
            ProfileSyncPhase.Uploading => $"HearthPulse: отправка · в очереди {status.PendingEvents}",
            ProfileSyncPhase.Idle => $"HearthPulse: синхронизировано · в очереди {status.PendingEvents}",
            ProfileSyncPhase.BackingOff => $"HearthPulse: повторная попытка · {status.PendingEvents}",
            ProfileSyncPhase.AuthorizationRequired => "HearthPulse: требуется повторное подключение",
            _ => $"HearthPulse: {status.Phase}",
        };
        RenderAccountState();
    }

    internal void SetLinkUpdate(ProfileLinkUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        Dispatcher.VerifyAccess();
        _linkUpdate = update;
        if (update.VerificationUri is not null)
        {
            _verificationUri = update.VerificationUri;
        }

        RenderAccountState();
    }

    public void SetRuntimeStatus(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        Dispatcher.VerifyAccess();
        TrackingStatus.Text = status;
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var filtered = _allMatches.Where(row =>
            HistoryNavigation.Includes(_navigation.Mode, row.Match.Mode) &&
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

    private void OnNavigate(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button selected)
        {
            return;
        }

        _navigation = HistoryNavigation.Resolve(selected.CommandParameter as string);
        var page = (int)_navigation.Page;
        var pages = new FrameworkElement[] { OverviewPage, MatchesPage, DecksPage, ProfilePage };
        var buttons = new[]
        {
            NavOverview,
            NavAllMatches,
            NavStandard,
            NavWild,
            NavArena,
            NavBattlegrounds,
            NavDecks,
            NavProfile,
        };
        for (var index = 0; index < pages.Length; index++)
        {
            pages[index].Visibility = index == page ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var button in buttons)
        {
            button.Tag = ReferenceEquals(button, selected) ? "selected" : null;
        }

        PageTitle.Text = _navigation.Title;
        PageSubtitle.Text = _navigation.Subtitle;
        if (_navigation.Page == HistoryPage.Matches)
        {
            ApplyFilter();
        }

        eventArgs.Handled = true;
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        _ = sender;
        if (eventArgs.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        eventArgs.Handled = true;
        WindowState = WindowState.Minimized;
    }

    private void OnToggleMaximize(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        eventArgs.Handled = true;
        ToggleMaximize();
    }

    private void OnClose(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        eventArgs.Handled = true;
        Close();
    }

    private void OnWindowStateChanged(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (MaximizeButton is not null)
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "Восстановить" : "Развернуть";
        }
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnAccountAction(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        eventArgs.Handled = true;
        if (HearthPulseAccountViewState.Create(_syncStatus, _linkUpdate).IsDisconnectAction)
        {
            UnlinkRequested?.Invoke();
        }
        else
        {
            LinkRequested?.Invoke();
        }
    }

    private void OnCancelLink(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        eventArgs.Handled = true;
        CancelLinkRequested?.Invoke();
    }

    private void OnOpenVerification(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        eventArgs.Handled = true;
        if (_verificationUri is not null)
        {
            VerificationPageRequested?.Invoke(_verificationUri);
        }
    }

    private void RenderAccountState()
    {
        var viewState = HearthPulseAccountViewState.Create(_syncStatus, _linkUpdate);
        AccountCodePanel.Visibility = viewState.ShowCode ? Visibility.Visible : Visibility.Collapsed;
        AccountCancelButton.Visibility = viewState.ShowCancel ? Visibility.Visible : Visibility.Collapsed;
        AccountActionButton.IsEnabled = viewState.IsActionEnabled;
        AccountActionButton.Content = viewState.ActionText;
        AccountCode.Text = viewState.UserCode;
        AccountExpires.Text = viewState.ExpiresText;
        AccountTitle.Text = viewState.Title;
        AccountDescription.Text = viewState.Description;
    }
}
