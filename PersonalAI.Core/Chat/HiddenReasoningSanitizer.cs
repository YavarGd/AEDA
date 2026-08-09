using System.Text;

namespace PersonalAI.Core.Chat;

/// <summary>
/// Single source of truth for stripping provider "hidden reasoning" blocks
/// (<c>&lt;think&gt;</c> / <c>&lt;analysis&gt;</c>) out of assistant text before it reaches any
/// user-visible or durable surface. Assist and General Chat both route through here so the two
/// surfaces cannot drift apart on what counts as safe to disclose.
///
/// Two modes exist because a streaming buffer and a finished response need different answers at
/// the very end of the text:
/// <list type="bullet">
/// <item><description><see cref="SanitizeVisible"/> treats the buffer as possibly incomplete.
/// A trailing fragment that could still grow into an opening tag (for example a chunk boundary
/// that split <c>&lt;think&gt;</c> into <c>"&lt;thi"</c> + <c>"nk&gt;secret"</c>) is withheld, so
/// neither the tag nor the reasoning payload behind it is ever briefly surfaced.</description></item>
/// <item><description><see cref="SanitizeCompleted"/> treats the buffer as final. No further
/// chunks can arrive, so a trailing fragment is ordinary text and is kept.</description></item>
/// </list>
/// An opened block whose closing tag has not arrived is withheld to the end of the buffer in both
/// modes, matching the pre-existing Assist behaviour for a truncated provider response.
/// </summary>
public static class HiddenReasoningSanitizer
{
    private static readonly string[] OpenTags = ["<think>", "<analysis>"];
    private static readonly string[] CloseTags = ["</think>", "</analysis>"];

    /// <summary>
    /// Safe text for a completed response. Used for persistence, replayed model history,
    /// reopened conversations, and clipboard copies.
    /// </summary>
    public static string SanitizeCompleted(string? value) =>
        Scan(value, keepTrailingTagFragment: true).Trim();

    /// <summary>
    /// Safe text for a response that is still streaming. Used for the live message content and
    /// therefore for the markdown renderer and accessibility announcements built from it.
    /// Whitespace is preserved so incremental chunks read naturally as they arrive.
    /// </summary>
    public static string SanitizeVisible(string? value) =>
        Scan(value, keepTrailingTagFragment: false);

    private static string Scan(string? value, bool keepTrailingTagFragment)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var index = 0;

        while (index < value.Length)
        {
            var open = value.IndexOf('<', index);
            if (open < 0)
            {
                builder.Append(value, index, value.Length - index);
                break;
            }

            builder.Append(value, index, open - index);

            var openTagLength = MatchOpenTag(value, open);
            if (openTagLength > 0)
            {
                var afterClose = IndexAfterCloseTag(value, open + openTagLength);
                if (afterClose < 0)
                {
                    // The block is still open. Everything from the opening tag onwards is
                    // reasoning until proven otherwise, so none of it may be disclosed.
                    return builder.ToString();
                }

                index = afterClose;
                continue;
            }

            if (IsTrailingTagFragment(value, open))
            {
                return keepTrailingTagFragment
                    ? builder.Append(value, open, value.Length - open).ToString()
                    : builder.ToString();
            }

            // An ordinary '<' — markup such as <code> or <xml> is untouched.
            builder.Append('<');
            index = open + 1;
        }

        return builder.ToString();
    }

    private static int MatchOpenTag(string value, int index)
    {
        foreach (var tag in OpenTags)
        {
            if (StartsWithAt(value, index, tag, tag.Length))
            {
                return tag.Length;
            }
        }

        return 0;
    }

    private static int IndexAfterCloseTag(string value, int startIndex)
    {
        var best = -1;
        foreach (var tag in CloseTags)
        {
            var found = value.IndexOf(tag, startIndex, StringComparison.OrdinalIgnoreCase);
            if (found >= 0 && (best < 0 || found < best))
            {
                best = found + tag.Length;
            }
        }

        return best;
    }

    /// <summary>
    /// True when the buffer ends part-way through what could still become an opening tag.
    /// A fragment can only qualify if it runs to the end of the buffer, so this never withholds
    /// text that already has more characters behind it.
    /// </summary>
    private static bool IsTrailingTagFragment(string value, int index)
    {
        var remaining = value.Length - index;
        foreach (var tag in OpenTags)
        {
            if (remaining < tag.Length && StartsWithAt(value, index, tag, remaining))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StartsWithAt(string value, int index, string tag, int length) =>
        index + length <= value.Length &&
        string.Compare(value, index, tag, 0, length, StringComparison.OrdinalIgnoreCase) == 0;
}

/// <summary>
/// Holds the raw provider stream privately and exposes only sanitized projections of it, so a
/// caller cannot accidentally reach for the unfiltered text. See
/// <see cref="HiddenReasoningSanitizer"/> for the difference between the two projections.
/// </summary>
public sealed class HiddenReasoningBuffer
{
    private readonly StringBuilder _raw = new();

    public void Append(string? chunk)
    {
        if (!string.IsNullOrEmpty(chunk))
        {
            _raw.Append(chunk);
        }
    }

    /// <summary>Safe text while the response is still streaming.</summary>
    public string VisibleText => HiddenReasoningSanitizer.SanitizeVisible(_raw.ToString());

    /// <summary>Safe text for persistence, history replay, and reopened conversations.</summary>
    public string CompletedText => HiddenReasoningSanitizer.SanitizeCompleted(_raw.ToString());
}
