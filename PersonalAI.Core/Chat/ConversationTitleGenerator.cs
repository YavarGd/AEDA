namespace PersonalAI.Core.Chat;

public static class ConversationTitleGenerator
{
    public const int MaxTitleLength = 60;

    public static string CreateTitle(string firstUserMessage)
    {
        var normalized = Normalize(firstUserMessage);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "New chat";
        }

        normalized = RemoveQuestionPreamble(normalized);
        normalized = RemoveTrailingPurpose(normalized);
        normalized = TrimSentenceEnding(normalized);

        return Shorten(normalized, MaxTitleLength);
    }

    public static string CreatePreview(string firstUserMessage)
    {
        var normalized = Normalize(firstUserMessage);
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : Shorten(normalized, 90);
    }

    private static string Normalize(string text)
    {
        var lines = text
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(RemoveMarkdownMarker)
            .Where(line => line.Length > 0);

        return string.Join(
            ' ',
            string.Join(' ', lines)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string RemoveMarkdownMarker(string line)
    {
        var trimmed = line.TrimStart('#', ' ', '-', '*', '+');
        if (trimmed.Length >= 2 &&
            char.IsDigit(trimmed[0]) &&
            trimmed[1] == '.')
        {
            trimmed = trimmed[2..].TrimStart();
        }

        return trimmed.Trim();
    }

    private static string RemoveQuestionPreamble(string text)
    {
        const string helpPrefix = "Can you help me ";
        if (text.StartsWith(helpPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = text[helpPrefix.Length..];
            return string.IsNullOrWhiteSpace(remainder)
                ? text
                : char.ToUpperInvariant(remainder[0]) + remainder[1..];
        }

        return text;
    }

    private static string RemoveTrailingPurpose(string text)
    {
        var markers = new[]
        {
            " in one sentence",
            " and explain",
            " and summarize"
        };

        foreach (var marker in markers)
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                return text[..index];
            }
        }

        return text;
    }

    private static string TrimSentenceEnding(string text) =>
        text.Trim().TrimEnd('.', '?', '!', ':', ';');

    private static string Shorten(string normalized, int maxLength)
    {
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        var limit = Math.Max(1, maxLength - 3);
        var cut = normalized.LastIndexOf(' ', limit);
        if (cut < maxLength / 2)
        {
            cut = limit;
        }

        return normalized[..cut].TrimEnd() + "...";
    }
}
