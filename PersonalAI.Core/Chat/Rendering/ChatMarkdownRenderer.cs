using System.Text;
using System.Text.RegularExpressions;

namespace PersonalAI.Core.Chat.Rendering;

public sealed partial class ChatMarkdownRenderer
{
    public static ChatMarkdownRenderer Shared { get; } = new();

    public RenderedChatContent Render(string markdown)
    {
        markdown ??= string.Empty;
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var blocks = new List<ChatRenderBlock>();
        var paragraph = new List<string>();
        var plain = new StringBuilder();

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
            {
                return;
            }

            var text = string.Join(" ", paragraph).Trim();
            if (text.Length > 0)
            {
                blocks.Add(new ChatParagraphBlock(ParseInlines(text)));
                AppendPlain(plain, text);
            }

            paragraph.Clear();
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                var language = SanitizeLanguage(trimmed[3..].Trim());
                var code = new StringBuilder();
                var closed = false;

                for (index++; index < lines.Length; index++)
                {
                    if (lines[index].Trim().StartsWith("```", StringComparison.Ordinal))
                    {
                        closed = true;
                        break;
                    }

                    if (code.Length > 0)
                    {
                        code.AppendLine();
                    }

                    code.Append(lines[index]);
                }

                var codeText = code.ToString();
                blocks.Add(new ChatCodeBlock(language, codeText));
                AppendPlain(plain, codeText);

                if (!closed)
                {
                    break;
                }

                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            if (IsHorizontalRule(trimmed))
            {
                FlushParagraph();
                blocks.Add(new ChatHorizontalRuleBlock());
                continue;
            }

            var heading = HeadingRegex().Match(trimmed);
            if (heading.Success)
            {
                FlushParagraph();
                var text = heading.Groups["text"].Value.Trim();
                blocks.Add(new ChatHeadingBlock(
                    Math.Min(6, heading.Groups["marks"].Value.Length),
                    ParseInlines(text)));
                AppendPlain(plain, text);
                continue;
            }

            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                FlushParagraph();
                var quoteLines = new List<string>();
                while (index < lines.Length &&
                       lines[index].TrimStart().StartsWith(">", StringComparison.Ordinal))
                {
                    quoteLines.Add(lines[index].TrimStart()[1..].TrimStart());
                    index++;
                }

                index--;
                var text = string.Join(" ", quoteLines).Trim();
                blocks.Add(new ChatQuoteBlock(ParseInlines(text)));
                AppendPlain(plain, text);
                continue;
            }

            var list = TryReadList(lines, ref index);
            if (list is not null)
            {
                FlushParagraph();
                blocks.Add(list);
                foreach (var item in list.Items)
                {
                    AppendPlain(plain, InlineText(item.Inlines));
                }

                continue;
            }

            paragraph.Add(trimmed);
        }

        FlushParagraph();

        if (blocks.Count == 0 && normalized.Length > 0)
        {
            blocks.Add(new ChatParagraphBlock([new ChatTextInline(normalized)]));
            plain.Append(normalized);
        }

        return new RenderedChatContent(blocks, plain.ToString().Trim());
    }

    public IReadOnlyList<ChatInline> ParseInlines(string text)
    {
        if (text.IndexOfAny(['[', '`', '*']) < 0)
        {
            return [new ChatTextInline(text)];
        }

        var inlines = new List<ChatInline>();
        var index = 0;

        while (index < text.Length)
        {
            var link = LinkRegex().Match(text, index);
            var code = InlineCodeRegex().Match(text, index);
            var bold = BoldRegex().Match(text, index);
            var italic = ItalicRegex().Match(text, index);
            var next = new[] { link, code, bold, italic }
                .Where(match => match.Success)
                .OrderBy(match => match.Index)
                .FirstOrDefault();

            if (next is null)
            {
                AddText(inlines, text[index..]);
                break;
            }

            if (next.Index > index)
            {
                AddText(inlines, text[index..next.Index]);
            }

            if (next == link)
            {
                var uri = next.Groups["uri"].Value.Trim();
                inlines.Add(new ChatLinkInline(
                    next.Groups["text"].Value,
                    uri,
                    IsSafeUri(uri)));
            }
            else if (next == code)
            {
                inlines.Add(new ChatCodeInline(next.Groups["text"].Value));
            }
            else if (next == bold)
            {
                inlines.Add(new ChatEmphasisInline(next.Groups["text"].Value, true, false));
            }
            else
            {
                inlines.Add(new ChatEmphasisInline(next.Groups["text"].Value, false, true));
            }

            index = next.Index + next.Length;
        }

        return inlines;
    }

    public static bool IsSafeUri(string uri)
    {
        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp ||
             parsed.Scheme == Uri.UriSchemeHttps);
    }

    private static ChatListBlock? TryReadList(string[] lines, ref int index)
    {
        var startIndex = index;
        var items = new List<ChatListItem>();
        bool? ordered = null;

        for (; index < lines.Length; index++)
        {
            var match = ListRegex().Match(lines[index]);
            if (!match.Success)
            {
                break;
            }

            var itemOrdered = match.Groups["ordered"].Success;
            ordered ??= itemOrdered;
            if (ordered.Value != itemOrdered)
            {
                break;
            }

            var spaces = match.Groups["indent"].Value.Length;
            items.Add(new ChatListItem(
                Math.Min(4, spaces / 2),
                Shared.ParseInlines(match.Groups["text"].Value.Trim())));
        }

        if (items.Count == 0)
        {
            index = startIndex;
            return null;
        }

        index--;
        return new ChatListBlock(ordered ?? false, items);
    }

    private static bool IsHorizontalRule(string trimmed) =>
        trimmed is "---" or "***" or "___";

    private static string SanitizeLanguage(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Take(40)
            .Where(character => char.IsLetterOrDigit(character) ||
                character is '#' or '+' or '-' or '_' or '.')
            .ToArray());
    }

    private static void AddText(List<ChatInline> inlines, string text)
    {
        if (text.Length > 0)
        {
            inlines.Add(new ChatTextInline(text));
        }
    }

    private static string InlineText(IEnumerable<ChatInline> inlines) =>
        string.Concat(inlines.Select(inline => inline.Text));

    private static void AppendPlain(StringBuilder builder, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append(text);
    }

    [GeneratedRegex(@"^(?<marks>#{1,6})\s+(?<text>.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(?<indent>\s*)((?<ordered>\d+\.)|[-*+])\s+(?<text>.+)$")]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"\[(?<text>[^\]]+)\]\((?<uri>[^)]+)\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"`(?<text>[^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\*\*(?<text>.+?)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"(?<!\*)\*(?<text>[^*]+)\*(?!\*)")]
    private static partial Regex ItalicRegex();
}
