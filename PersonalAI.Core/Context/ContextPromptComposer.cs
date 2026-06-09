using PersonalAI.Core.Chat;

namespace PersonalAI.Core.Context;

public static class ContextPromptComposer
{
    public static IReadOnlyList<ChatMessage> Compose(
        IEnumerable<ChatMessage> previousMessages,
        string prompt,
        ActiveApplicationContext? attachedContext)
    {
        var messages = previousMessages.ToList();

        if (attachedContext is not null)
        {
            messages.Add(new ChatMessage(
                ChatRole.System,
                ContextFormatter.FormatPromptBlock(attachedContext)));
        }

        messages.Add(new ChatMessage(ChatRole.User, prompt));
        return messages;
    }
}
