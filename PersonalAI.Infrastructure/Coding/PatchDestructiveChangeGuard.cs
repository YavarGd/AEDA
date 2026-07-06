using PersonalAI.Core.Coding;

namespace PersonalAI.Infrastructure.Coding;

internal static class PatchDestructiveChangeGuard
{
    public const string UnsafeLargeDeletionCode = "unsafe_large_deletion";
    public const string PartialProposedContentCode = "partial_proposed_content";
    public const string InvalidFileShapeCode = "invalid_file_shape";

    public static void RejectUnsafeEdit(PatchProposalFileEdit edit)
    {
        if (edit.ChangeKind != PatchProposalFileChangeKind.Modify ||
            edit.OriginalContent is null ||
            edit.ProposedContent is null)
        {
            return;
        }

        RejectUnsafeReplacement(
            edit.RelativePath,
            edit.OriginalContent,
            edit.ProposedContent);
    }

    public static bool IsUnsafeReplacement(PatchProposalFile file)
    {
        if (file.ChangeKind != PatchProposalFileChangeKind.Modify ||
            file.OriginalContent is null ||
            file.ProposedContent is null)
        {
            return false;
        }

        try
        {
            RejectUnsafeReplacement(
                file.RelativePath,
                file.OriginalContent,
                file.ProposedContent);
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void RejectUnsafeReplacement(
        string relativePath,
        string original,
        string proposed)
    {
        if (LooksLikeCSharpInProjectFile(relativePath, proposed))
        {
            throw new InvalidOperationException(InvalidFileShapeCode);
        }

        var oldLength = original.Length;
        if (oldLength < 4_000)
        {
            return;
        }

        var newLength = proposed.Length;
        var oldLines = CountLines(original);
        var newLines = CountLines(proposed);
        if ((newLength < oldLength / 4 && oldLength - newLength > 2_000) ||
            (oldLines >= 200 && newLines < oldLines / 4))
        {
            throw new InvalidOperationException(
                LooksLikePartialSnippet(relativePath, proposed)
                    ? PartialProposedContentCode
                    : UnsafeLargeDeletionCode);
        }
    }

    private static bool LooksLikePartialSnippet(string relativePath, string proposed)
    {
        var extension = Path.GetExtension(relativePath);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) &&
            !proposed.Contains("class ", StringComparison.Ordinal) &&
            !proposed.Contains("namespace ", StringComparison.Ordinal) &&
            proposed.Contains('(') &&
            proposed.Contains(')');
    }

    private static bool LooksLikeCSharpInProjectFile(string relativePath, string proposed) =>
        Path.GetExtension(relativePath).Equals(".csproj", StringComparison.OrdinalIgnoreCase) &&
        (proposed.Contains(" class ", StringComparison.Ordinal) ||
            proposed.Contains("private ", StringComparison.Ordinal) ||
            proposed.Contains("public ", StringComparison.Ordinal));

    private static int CountLines(string value) =>
        value.Count(ch => ch == '\n') + (value.Length == 0 ? 0 : 1);
}
