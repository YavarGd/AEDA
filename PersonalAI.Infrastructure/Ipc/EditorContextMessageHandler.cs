using PersonalAI.Core.Editor;

namespace PersonalAI.Infrastructure.Ipc;

public sealed class EditorContextMessageHandler(
    Action<EditorContextEnvelope> attachContext,
    Action openPersonalAi)
{
    public void Handle(EditorContextEnvelope envelope)
    {
        if (envelope.Command.Equals(
                EditorContextCommands.OpenPersonalAi,
                StringComparison.OrdinalIgnoreCase))
        {
            openPersonalAi();
            return;
        }

        attachContext(envelope);
    }
}
