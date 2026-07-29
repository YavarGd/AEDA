using System.Text;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Chat.Rendering;
using PersonalAI.Infrastructure.Chat;

namespace PersonalAI.Desktop.Avalonia.Views.Chat;

/// <summary>
/// View-owned text presentation for the chat surface. Every status is expressed as
/// words so state is never communicated by colour alone, and assistant markdown gets
/// a plain-text form for screen readers.
/// </summary>
public static class ChatPresentation
{
    public static string DescribeRole(ChatRole role) => role switch
    {
        ChatRole.User => "You",
        ChatRole.Assistant => "AEDA",
        ChatRole.Tool => "Workspace tool",
        _ => "System"
    };

    /// <summary>
    /// A short textual status label. A safe failure message, when present, wins because
    /// it is the more specific information for the reader.
    /// </summary>
    public static string DescribeStatus(ChatStatus status, string? statusMessage)
    {
        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            return statusMessage.Trim();
        }

        return status switch
        {
            ChatStatus.Ready => "Ready",
            ChatStatus.Connecting => "Connecting",
            ChatStatus.Generating => "Generating response",
            ChatStatus.Completed => "Response complete",
            ChatStatus.Cancelled => "Response cancelled",
            ChatStatus.Failed => "Response failed",
            _ => "Ready"
        };
    }

    /// <summary>
    /// Flattens rendered markdown to readable plain text for AutomationProperties.Name.
    /// Unsupported or unsafe constructs stay readable rather than disappearing.
    /// </summary>
    public static string ToAccessibleText(RenderedChatContent? content)
    {
        if (content is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(content.PlainText))
        {
            return content.PlainText.Trim();
        }

        var builder = new StringBuilder();
        foreach (var block in content.Blocks)
        {
            var text = block switch
            {
                ChatParagraphBlock paragraph => FlattenInlines(paragraph.Inlines),
                ChatHeadingBlock heading => FlattenInlines(heading.Inlines),
                ChatQuoteBlock quote => FlattenInlines(quote.Inlines),
                ChatCodeBlock code => code.Code,
                ChatListBlock list => string.Join(
                    " ",
                    list.Items.Select(item => FlattenInlines(item.Inlines))),
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(text.Trim());
        }

        return builder.ToString();
    }

    /// <summary>
    /// Links that are not http/https are shown as readable text including the target so
    /// nothing is silently dropped and nothing unsafe becomes clickable.
    /// </summary>
    public static string DescribeUnsafeLink(ChatLinkInline link) =>
        $"{link.Text} ({link.Uri})";

    private static string FlattenInlines(IEnumerable<ChatInline> inlines) =>
        string.Concat(inlines.Select(inline => inline switch
        {
            ChatLinkInline { IsSafe: false } link => DescribeUnsafeLink(link),
            _ => inline.Text
        }));
}
