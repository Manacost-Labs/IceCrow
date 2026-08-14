using System.Collections.ObjectModel;
using System.Windows;
using IceCrow.Hearthstone.Logs;
using IceCrow.Live;

namespace IceCrow.App;

public partial class MainWindow : Window
{
    private const int MaximumVisibleLines = 20;
    private readonly ObservableCollection<string> _powerLogLines = [];

    public MainWindow()
    {
        InitializeComponent();
        PowerLogList.ItemsSource = _powerLogLines;
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
        LiveTrackingState.Text = $"Tracking: {diagnostics.TrackingState}";
        LiveLastUpdate.Text = diagnostics.LastStateUpdateTimestamp is DateTimeOffset timestamp
            ? $"Last update: {timestamp:HH:mm:ss.fff}"
            : "Last update: -";
    }
}
