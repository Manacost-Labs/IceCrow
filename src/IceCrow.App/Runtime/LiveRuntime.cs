using System.Text;
using System.IO;
using IceCrow.Hearthstone.Logs;
using IceCrow.Live;

namespace IceCrow.App.Runtime;

internal sealed class LiveRuntime : IAsyncDisposable
{
    private readonly LogConfigManager _logConfigManager = new();
    private readonly HearthstoneLogLocator _logLocator = new();
    private readonly LiveTrackingCoordinator _coordinator;
    private readonly PowerLogTailer _tailer;
    private readonly Action<LiveTrackingUpdate> _onProcessed;
    private readonly Action<Exception> _onRecoverableError;
    private readonly Action<string> _onStatus;

    public LiveRuntime(
        Action<LiveTrackingUpdate> onProcessed,
        Action<Exception> onRecoverableError,
        Action<string> onStatus,
        IAppliedMatchEventObserver? appliedEventObserver = null)
    {
        ArgumentNullException.ThrowIfNull(onProcessed);
        ArgumentNullException.ThrowIfNull(onRecoverableError);
        ArgumentNullException.ThrowIfNull(onStatus);
        _coordinator = new LiveTrackingCoordinator(
            appliedEventObserver: appliedEventObserver);
        _onProcessed = onProcessed;
        _onRecoverableError = onRecoverableError;
        _onStatus = onStatus;
        _tailer = new PowerLogTailer(_logLocator);
        _logLocator.RecoverableError += OnRecoverableError;
        _tailer.RecoverableError += OnRecoverableError;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var changed = await _logConfigManager
                .EnsurePowerLoggingAsync(_logLocator, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _onStatus(changed
                ? "Power logging configured. Hearthstone may need a restart before new settings take effect."
                : "Power logging configuration is ready.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException)
        {
            _onRecoverableError(exception);
        }

        var tailTask = _tailer.RunAsync(cancellationToken);
        var consumeTask = _coordinator.RunAsync(_tailer.Lines, _onProcessed, cancellationToken);
        await Task.WhenAll(tailTask, consumeTask).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _logLocator.RecoverableError -= OnRecoverableError;
        _tailer.RecoverableError -= OnRecoverableError;
        _logConfigManager.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnRecoverableError(Exception exception) => _onRecoverableError(exception);
}
