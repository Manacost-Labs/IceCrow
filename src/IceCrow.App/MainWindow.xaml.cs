using System.Collections.ObjectModel;
using System.Windows;
using IceCrow.Hearthstone.Logs;

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
}
