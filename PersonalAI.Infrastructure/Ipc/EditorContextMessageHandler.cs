using PersonalAI.Core.Editor;

namespace PersonalAI.Infrastructure.Ipc;

public sealed class EditorContextMessageHandler(
    Action<EditorContextEnvelope> attachContext,
    Action openPersonalAi,
    Func<EditorContextEnvelope, CancellationToken, Task<EditorContextHandlerResult>>? handleEditorCommand = null)
{
    public async Task<EditorContextHandlerResult> HandleAsync(
        EditorContextEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Command.Equals(
                EditorContextCommands.OpenPersonalAi,
                StringComparison.OrdinalIgnoreCase))
        {
            openPersonalAi();
            return EditorContextHandlerResult.Success("opened");
        }

        attachContext(envelope);

        if (envelope.Command.Equals(
                EditorContextCommands.UpdateSelectionContext,
                StringComparison.OrdinalIgnoreCase))
        {
            return EditorContextHandlerResult.Success("context updated");
        }

        if (handleEditorCommand is null)
        {
            return EditorContextHandlerResult.Success("ok");
        }

        return await handleEditorCommand(envelope, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record EditorContextHandlerResult(bool Ok, string Message)
{
    public static EditorContextHandlerResult Success(string message) => new(true, message);

    public static EditorContextHandlerResult Failure(string message) => new(false, message);
}
