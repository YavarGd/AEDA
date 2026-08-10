namespace PersonalAI.Desktop.Avalonia;

internal sealed class AvaloniaShutdownCoordinator(
    Func<ValueTask> disposeResources,
    Action<int> shutdown,
    TimeSpan? cleanupTimeout = null)
{
    internal static readonly TimeSpan DefaultCleanupTimeout = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private readonly TimeSpan _cleanupTimeout = cleanupTimeout ?? DefaultCleanupTimeout;
    private Task? _shutdownTask;

    public Task BeginAsync(int exitCode = 0)
    {
        lock (_gate)
        {
            return _shutdownTask ??= ShutdownAsync(exitCode);
        }
    }

    private async Task ShutdownAsync(int exitCode)
    {
        try
        {
            await disposeResources().AsTask().WaitAsync(_cleanupTimeout);
        }
        catch
        {
            // Cleanup is best-effort and bounded. Process shutdown is the final quarantine
            // for an uncooperative native call that outlives its provider's own timeout.
        }
        finally
        {
            shutdown(exitCode);
        }
    }
}
