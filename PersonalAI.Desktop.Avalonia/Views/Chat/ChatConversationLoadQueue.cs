namespace PersonalAI.Desktop.Avalonia.Views.Chat;

/// <summary>
/// Serializes conversation loads.
/// <para>
/// The chat view model publishes its result by posting to the UI thread, so a load has
/// already queued its mutation by the time its task completes. Checking "am I still the
/// newest?" afterwards therefore cannot undo anything. Instead each request waits for its
/// predecessor to finish before it starts, so the posts are enqueued in request order and
/// the newest request is always the last to apply. The previous request is also cancelled,
/// so in practice it usually abandons before publishing at all.
/// </para>
/// <para>
/// Only the request that is still current may run the visible fallback: a superseded
/// request must stay silent so it cannot pull the display back to an older selection.
/// </para>
/// <para>
/// Enqueue and Invalidate are expected to be called from the UI thread; no locking is
/// needed. Superseded requests observe cancellation immediately, so the chain drains
/// rather than accumulating work.
/// </para>
/// </summary>
public sealed class ChatConversationLoadQueue
{
    private Task _previous = Task.CompletedTask;
    private CancellationTokenSource? _cancellation;

    /// <summary>
    /// Cancels the request in flight and queues <paramref name="load"/> behind it.
    /// <paramref name="onAbandoned"/> runs only when the cancelled or failed request is
    /// still the current one, so the caller can put its visible state back in agreement
    /// with the model without fighting a newer selection.
    /// </summary>
    public Task Enqueue(
        Func<CancellationToken, Task> load,
        Action onAbandoned)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(onAbandoned);

        _cancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        _previous = RunAsync(_previous, load, onAbandoned, cancellation);
        return _previous;
    }

    /// <summary>
    /// Abandons any queued or in-flight load without running the visible fallback. Used
    /// when an intent replaces the selection entirely, such as starting a new chat or
    /// swapping the view model.
    /// </summary>
    public void Invalidate()
    {
        _cancellation?.Cancel();
        _cancellation = null;
    }

    private async Task RunAsync(
        Task previous,
        Func<CancellationToken, Task> load,
        Action onAbandoned,
        CancellationTokenSource cancellation)
    {
        try
        {
            // Wait for the predecessor so publication order matches request order. Its
            // outcome is not this request's concern.
            try
            {
                await previous;
            }
            catch
            {
            }

            if (cancellation.IsCancellationRequested)
            {
                Abandon(onAbandoned, cancellation);
                return;
            }

            await load(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Abandon(onAbandoned, cancellation);
        }
        catch
        {
            // Contained: this task is not awaited by the caller, so nothing may escape.
            Abandon(onAbandoned, cancellation);
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
            }

            cancellation.Dispose();
        }
    }

    /// <summary>
    /// Runs the fallback only for the current request; a superseded one fails silently.
    /// </summary>
    private void Abandon(Action onAbandoned, CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(_cancellation, cancellation))
        {
            onAbandoned();
        }
    }
}
