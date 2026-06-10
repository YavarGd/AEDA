using System.Text;
using PersonalAI.Core.Chat;

namespace PersonalAI.Core.Context;

public static class AttachedContextPromptComposer
{
    public const int MaxContextPayloadCharacters = 12_000;
    public const int MaxTotalContextCharacters = 24_000;

    public static IReadOnlyList<ChatMessage> Compose(
        IEnumerable<ChatMessage> previousMessages,
        string userPrompt,
        IEnumerable<AttachedContextItem> attachedContexts)
    {
        ArgumentNullException.ThrowIfNull(previousMessages);
        ArgumentNullException.ThrowIfNull(attachedContexts);

        var messages = previousMessages.ToList();
        var contextSnapshot = attachedContexts.ToArray();
        var contextBlock = FormatContextBlock(contextSnapshot);

        if (!string.IsNullOrWhiteSpace(contextBlock))
        {
            messages.Add(new ChatMessage(ChatRole.System, contextBlock));
        }

        var images = contextSnapshot
            .SelectMany(context => context.Images)
            .ToArray();

        messages.Add(new ChatMessage(ChatRole.User, userPrompt, images));
        return messages;
    }

    public static string FormatContextBlock(
        IEnumerable<AttachedContextItem> attachedContexts)
    {
        ArgumentNullException.ThrowIfNull(attachedContexts);

        var builder = new StringBuilder();
        var written = 0;

        foreach (var context in attachedContexts)
        {
            var payload = Truncate(context.ProviderPayload, MaxContextPayloadCharacters);
            var header = new StringBuilder();
            header.AppendLine(
                $"Attached context: {context.Type} - {context.DisplayTitle}");
            header.AppendLine($"Source: {context.SourceName}");
            header.AppendLine("---");

            var footer = new StringBuilder();
            footer.AppendLine();
            footer.AppendLine("---");
            footer.AppendLine(
                "Use this context only if it is relevant to the user's request.");

            var blockBuilder = new StringBuilder();
            blockBuilder.Append(header);
            blockBuilder.Append(payload);
            blockBuilder.Append(footer);
            var block = blockBuilder.ToString();

            if (written + block.Length > MaxTotalContextCharacters)
            {
                var remaining = MaxTotalContextCharacters - written;

                if (remaining <= 0)
                {
                    break;
                }

                builder.Append(Truncate(block, remaining));
                break;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(block);
            written += block.Length;
        }

        return builder.ToString().TrimEnd();
    }

    private static string Truncate(string value, int maxCharacters)
    {
        if (value.Length <= maxCharacters)
        {
            return value;
        }

        const string marker = "\n[Context truncated]";
        var take = Math.Max(0, maxCharacters - marker.Length);
        return value[..take] + marker;
    }
}
