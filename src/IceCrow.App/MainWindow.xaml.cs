using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using IceCrow.App.Runtime;
using IceCrow.Hearthstone.Logs;
using IceCrow.Live;
using IceCrow.Infrastructure.ManacostApi;
using IceCrow.Overlay;
#if DEBUG
using IceCrow.App.DesignPreview;
#endif

namespace IceCrow.App;

public partial class MainWindow : Window
{
    private const int MaximumVisibleLines = 20;
    private readonly ObservableCollection<string> _powerLogLines = [];
#if DEBUG
    private DesignPreviewWindow? _designPreviewWindow;
#endif

    public MainWindow()
    {
        InitializeComponent();
        PowerLogList.ItemsSource = _powerLogLines;
#if !DEBUG
        DesignPreviewButton.Visibility = Visibility.Collapsed;
#endif
    }

    public void AddPowerLogLine(RawLogLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Dispatcher.VerifyAccess();

        _powerLogLines.Add($"{line.Timestamp:HH:mm:ss.fff}  {line.Content}");
        while (_powerLogLines.Count > MaximumVisibleLines)
        {
            _powerLogLines.RemoveAt(0);
        }

        if (_powerLogLines.Count > 0)
        {
            PowerLogList.ScrollIntoView(_powerLogLines[^1]);
        }
    }

    public void SetPowerLogStatus(string status)
    {
        Dispatcher.VerifyAccess();
        PowerLogStatus.Text = status;
    }

    public void SetBattlegroundsDiagnostics(
        bool isActive,
        int turn,
        string phase,
        int playerCount,
        int? currentOpponentPlayerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        Dispatcher.VerifyAccess();

        BattlegroundsActive.Text = isActive ? "BG ACTIVE" : "BG INACTIVE";
        BattlegroundsTurn.Text = $"Turn: {turn}";
        BattlegroundsPhase.Text = $"Phase: {phase}";
        BattlegroundsPlayers.Text = $"Players: {playerCount}";
        BattlegroundsOpponent.Text = currentOpponentPlayerId is int opponentPlayerId
            ? $"Opponent: PlayerId {opponentPlayerId}"
            : "Opponent: PlayerId -";
    }

    public void SetLiveTrackingDiagnostics(LiveTrackingDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Dispatcher.VerifyAccess();

        SetBattlegroundsDiagnostics(
            diagnostics.IsBattlegroundsActive,
            diagnostics.Turn,
            diagnostics.Phase.ToString(),
            diagnostics.LobbyCount,
            diagnostics.CurrentOpponentPlayerId);
        LiveRawLines.Text = $"Raw / parsed: {diagnostics.RawLinesReceived} / {diagnostics.ParsedEvents}";
        LiveRejectedLines.Text =
            $"Ignored / unknown / malformed: {diagnostics.Ignored} / {diagnostics.Unknown} / {diagnostics.Malformed}";
        LiveAppliedEvents.Text = diagnostics.BufferedEventsDropped == 0
            ? $"Applied: {diagnostics.TrackingEventsApplied}"
            : $"Applied: {diagnostics.TrackingEventsApplied} · buffered drops: {diagnostics.BufferedEventsDropped}";
        if (diagnostics.Warnings.HasFlag(LiveTrackingWarnings.CaptureObserverDetached))
        {
            LiveAppliedEvents.Text += " · capture observer detached";
        }
        LiveTrackingState.Text = $"Tracking: {diagnostics.TrackingState}";
        LiveLastUpdate.Text = diagnostics.LastStateUpdateTimestamp is DateTimeOffset timestamp
            ? $"Last update: {timestamp:HH:mm:ss.fff}"
            : "Last update: -";
    }

    public void SetTailerDiagnostics(PowerLogTailerDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Dispatcher.VerifyAccess();

        TailerResets.Text = diagnostics.LastResetReason is LogResetReason reason
            ? $"Full rereads: {diagnostics.FullRereadCount} · Last reset: {reason} at {diagnostics.LastResetAt:HH:mm:ss} (offset {diagnostics.LastResetOffset:N0}, length {diagnostics.LastResetObservedLength:N0})"
            : $"Full rereads: {diagnostics.FullRereadCount}";
    }

    public void SetManacostDataStatus(ManacostDataStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        Dispatcher.VerifyAccess();

        ManacostCache.Text = status.CacheReady
            ? $"Cache: Ready{(status.OfflineMode ? " (offline)" : string.Empty)}"
            : "Cache: Not available";
        ManacostVersion.Text = $"Data version: {status.DataVersion ?? "-"}";
        ManacostBuild.Text = $"Hearthstone build: {status.HearthstoneBuild ?? "-"}";
        ManacostSync.Text = status.LastSync is DateTimeOffset timestamp
            ? $"Last sync: {timestamp:yyyy-MM-dd HH:mm:ss}"
            : $"Last sync: -{(string.IsNullOrWhiteSpace(status.SyncError) ? string.Empty : $" · {status.SyncError}")}";
        ManacostCounts.Text =
            $"Cards / BG minions / heroes: {status.CardCount} / {status.BattlegroundsMinionCount} / {status.BattlegroundsHeroCount}";
    }

    public void SetDeckstringsStatus(string packageVersion, string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        Dispatcher.VerifyAccess();
        DeckstringsStatus.Text = $"Package: {packageVersion} · Status: {status}";
    }

    public void SetOverlayRenderDiagnostics(OverlayRenderDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Dispatcher.VerifyAccess();
        OverlayRenderStats.Text = diagnostics.ToString();
    }

    public void SetTelemetryStatus(bool consent, int queued, DateTimeOffset? lastUpload)
    {
        Dispatcher.VerifyAccess();
        TelemetryStatus.Text =
            $"Consent: {(consent ? "On" : "Off")} · Queued: {queued} · Last upload: {(lastUpload is null ? "-" : lastUpload.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}";
    }

    internal event Action<bool>? CaptureToggleChanged;

    internal void SetCaptureStatus(RecordingCaptureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        Dispatcher.VerifyAccess();

        var session = status.SessionPhase switch
        {
            RecordingSessionPhase.Recording =>
                $"Recording · events: {status.CurrentEventCount}",
            RecordingSessionPhase.WaitingForNextMatch => "Waiting",
            _ => "Off",
        };
        var persistence = status.PendingSaveCount > 0
            ? $"{status.PersistencePhase} · pending: {status.PendingSaveCount}"
            : status.PersistencePhase.ToString();
        CaptureStatus.Text =
            $"Enabled: {(status.IsEnabled ? "Yes" : "No")} · Session: {session} · Persistence: {persistence}";
        CaptureDetail.Text =
            $"Captures: {status.SavedCaptureCount} · Last: {status.LastSavedFileName ?? "-"}";
        CaptureMessages.Text = FormatCaptureMessages(status);
    }

    private static string FormatCaptureMessages(RecordingCaptureStatus status)
    {
        var lines = new List<string>(3);
        if (status.LastNotice is { } notice)
        {
            lines.Add($"Notice: {notice}");
        }

        if (status.LastWarning is { } warning)
        {
            lines.Add($"Warning: {warning}");
        }

        if (status.LastError is { } error)
        {
            lines.Add($"Error: {error}");
        }

        return lines.Count == 0 ? "-" : string.Join(Environment.NewLine, lines);
    }

    private void OnCaptureToggleChanged(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        eventArgs.Handled = true;
        CaptureToggleChanged?.Invoke(CaptureToggle.IsChecked == true);
    }

    private void OnOpenDesignPreviewClicked(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        eventArgs.Handled = true;
#if DEBUG
        if (_designPreviewWindow is not null)
        {
            _designPreviewWindow.Activate();
            return;
        }

        _designPreviewWindow = new DesignPreviewWindow { Owner = this };
        _designPreviewWindow.Closed += OnDesignPreviewClosed;
        _designPreviewWindow.Show();
#endif
    }

#if DEBUG
    private void OnDesignPreviewClosed(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (_designPreviewWindow is not null)
        {
            _designPreviewWindow.Closed -= OnDesignPreviewClosed;
            _designPreviewWindow = null;
        }
    }
#endif
}
