using PersonalAI.Infrastructure.Context;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class ExternalForegroundWindowMonitor(
    ForegroundWindowTracker foregroundWindowTracker,
    Func<nint> getOwnWindowHandle) : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _monitorTask;

    public void Start()
    {
        _monitorTask ??= Task.Run(MonitorAsync);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async Task MonitorAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(750));

            while (await timer.WaitForNextTickAsync(_cancellation.Token))
            {
                _ = foregroundWindowTracker.CaptureCurrentExternalWindow(
                    getOwnWindowHandle());
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
