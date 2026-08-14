namespace IceCrow.Telemetry;

public sealed class TelemetryConsent
{
    private int _enabled;

    public TelemetryConsent(bool enabled = false)
    {
        _enabled = enabled ? 1 : 0;
    }

    public event EventHandler? Changed;

    public bool IsEnabled => Volatile.Read(ref _enabled) != 0;

    public void SetEnabled(bool enabled)
    {
        var value = enabled ? 1 : 0;
        if (Interlocked.Exchange(ref _enabled, value) != value)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
