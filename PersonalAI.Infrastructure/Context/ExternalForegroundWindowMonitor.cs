#if WINDOWS
namespace PersonalAI.Infrastructure.Context;

public sealed class ExternalForegroundWindowMonitor(
    ForegroundWindowTracker foregroundWindowTracker,
    Func<nint> getOwnWindowHandle,
    TimeSpan? interval = null,
    Action? captureTick = null) : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _gate = new();
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromMilliseconds(750);
    private readonly Action _captureTick = captureTick ?? (() =>
        _ = foregroundWindowTracker.CaptureCurrentExternalWindow(getOwnWindowHandle()));
    private Task? _monitorTask;
    private Task? _disposeTask;
    private bool _disposed;

    public void Start()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _monitorTask ??= Task.Run(MonitorAsync);
            }
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is null)
            {
                _disposed = true;
                _cancellation.Cancel();
                _disposeTask = FinishDisposalAsync(_monitorTask);
            }

            return new ValueTask(_disposeTask);
        }
    }

    private async Task FinishDisposalAsync(Task? monitorTask)
    {
        if (monitorTask is not null)
        {
            await monitorTask;
        }

        _cancellation.Dispose();
    }

    private async Task MonitorAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(_cancellation.Token))
            {
                _captureTick();
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
    }
}
#endif
