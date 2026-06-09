namespace PersonalAI.Core.Chat;

public static class ConversationTitleGenerator
{
    public const int MaxTitleLength = 60;

    public static string CreateTitle(string firstUserMessage)
    {
        var normalized = string.Join(
            ' ',
            firstUserMessage
                .ReplaceLineEndings(" ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "New Chat";
        }

        if (normalized.Length <= MaxTitleLength)
        {
            return normalized;
        }

        return normalized[..(MaxTitleLength - 3)] + "...";
    }
}
