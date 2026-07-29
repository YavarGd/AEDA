namespace PersonalAI.Desktop.Avalonia.Views.Chat;

/// <summary>
/// Confirms that opening a conversation actually took effect.
/// <para>
/// The view model publishes by posting to the UI thread and silently does nothing when the
/// row is missing or a response is generating. Awaiting the open call alone therefore
/// proves nothing, so this waits on a UI-thread barrier queued behind that post and then
/// reads back the active conversation.
/// </para>
/// </summary>
public static class ChatConversationOpener
{
    /// <summary>
    /// Returns true only when <paramref name="requestedId"/> is the active conversation
    /// after the view model's queued mutation has run.
    /// </summary>
    public static async Task<bool> OpenAndConfirmAsync(
        Guid requestedId,
        Func<CancellationToken, Task> open,
        Func<Task> uiBarrier,
        Func<Guid?> readActiveConversationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(uiBarrier);
        ArgumentNullException.ThrowIfNull(readActiveConversationId);

        await open(cancellationToken);

        // Runs after the view model's post at the same dispatcher priority.
        await uiBarrier();

        cancellationToken.ThrowIfCancellationRequested();

        return readActiveConversationId() == requestedId;
    }
}
