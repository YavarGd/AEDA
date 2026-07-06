using System.Security.Cryptography;
using System.Text;

namespace PersonalAI.Infrastructure.Coding;

internal static class CodeTargetSnippetExtractor
{
    private const int MaxCandidateSnippets = 8;
    private const int MaxCandidateSnippetCharacters = 2_000;
    private const int MaxCandidateTotalCharacters = 8_000;
    private const int MaxPreviewCharacters = 360;

    public static IReadOnlyList<CodeTargetSnippet> Extract(string relativePath, string content)
    {
        if (!Path.GetExtension(relativePath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var snippets = new List<CodeTargetSnippet>();
        var total = 0;
        var search = 0;
        while (search < content.Length &&
               snippets.Count < MaxCandidateSnippets &&
               total < MaxCandidateTotalCharacters)
        {
            var index = content.IndexOf("private ", search, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            var lineStart = content.LastIndexOf('\n', Math.Max(0, index - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            var hasXmlDoc = HasXmlDocImmediatelyBefore(content, lineStart);
            var open = content.IndexOf('{', index);
            if (open < 0)
            {
                break;
            }

            var signature = content[index..open];
            if (!LooksLikeMethodSignature(signature))
            {
                search = index + 8;
                continue;
            }

            var close = FindMatchingBrace(content, open);
            if (close < 0)
            {
                break;
            }

            var end = close + 1;
            if (end < content.Length && content[end] == '\r')
            {
                end++;
            }

            if (end < content.Length && content[end] == '\n')
            {
                end++;
            }

            var snippet = content[lineStart..end];
            var lines = snippet.Count(ch => ch == '\n') + 1;
            if (!hasXmlDoc &&
                lines is >= 5 and <= 30 &&
                snippet.Length <= MaxCandidateSnippetCharacters &&
                (total + snippet.Length) <= MaxCandidateTotalCharacters)
            {
                total += snippet.Length;
                var previewSignature = Bound(signature.ReplaceLineEndings(" ").Trim(), 160);
                snippets.Add(new CodeTargetSnippet(
                    StableId(relativePath, GetLineNumber(content, lineStart), previewSignature, snippet),
                    relativePath.Replace('\\', '/'),
                    CreateDisplayName(previewSignature),
                    previewSignature,
                    GetLineNumber(content, lineStart),
                    lines,
                    snippet.Length,
                    hasXmlDoc,
                    Bound(snippet.Trim(), MaxPreviewCharacters),
                    snippet));
            }

            search = end;
        }

        return snippets;
    }

    private static bool LooksLikeMethodSignature(string signature) =>
        signature.Contains('(') &&
        signature.Contains(')') &&
        !signature.Contains(';') &&
        !signature.Contains('=') &&
        !signature.Contains("=>", StringComparison.Ordinal);

    private static bool HasXmlDocImmediatelyBefore(string content, int lineStart)
    {
        var previousEnd = lineStart - 1;
        while (previousEnd >= 0 &&
               (content[previousEnd] == '\r' ||
                content[previousEnd] == '\n' ||
                char.IsWhiteSpace(content[previousEnd])))
        {
            previousEnd--;
        }

        var previousStart = previousEnd < 0 ? 0 : content.LastIndexOf('\n', previousEnd) + 1;
        return content[previousStart..Math.Max(previousStart, previousEnd + 1)]
            .TrimStart()
            .StartsWith("///", StringComparison.Ordinal);
    }

    private static int FindMatchingBrace(string content, int open)
    {
        var depth = 0;
        for (var index = open; index < content.Length; index++)
        {
            if (content[index] == '{')
            {
                depth++;
            }
            else if (content[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static int GetLineNumber(string content, int offset) =>
        content[..Math.Clamp(offset, 0, content.Length)].Count(ch => ch == '\n') + 1;

    private static string CreateDisplayName(string signature)
    {
        var open = signature.IndexOf('(');
        if (open <= 0)
        {
            return Bound(signature, 80);
        }

        var before = signature[..open].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return before.Length == 0 ? Bound(signature, 80) : Bound(before[^1], 80);
    }

    private static string StableId(string relativePath, int startLine, string signature, string snippet)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{relativePath.Replace('\\', '/')}|{startLine}|{signature}|{snippet}"));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string Bound(string value, int maxCharacters) =>
        value.Length <= maxCharacters ? value : value[..maxCharacters].TrimEnd() + "...";
}

internal sealed record CodeTargetSnippet(
    string Id,
    string RelativePath,
    string DisplayName,
    string SignaturePreview,
    int StartLine,
    int LineCount,
    int ApproximateCharacters,
    bool AlreadyHasXmlDocumentation,
    string SafePreview,
    string Text);
