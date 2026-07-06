using PersonalAI.Core.Coding;

namespace PersonalAI.Infrastructure.Coding;

public sealed class UnifiedDiffBuilder : IUnifiedDiffBuilder
{
    private const int ContextLineCount = 3;

    public PatchProposalFile BuildFileDiff(
        PatchProposalFileEdit edit,
        int maxDiffCharacters = 200_000)
    {
        ValidatePath(edit.RelativePath);
        var original = edit.OriginalContent ?? string.Empty;
        var proposed = edit.ProposedContent ?? string.Empty;
        if (ContainsBinary(original) || ContainsBinary(proposed))
        {
            throw new InvalidOperationException("binary_patch_not_supported");
        }

        var oldLines = SplitLines(original);
        var newLines = SplitLines(proposed);
        var rows = BuildRows(oldLines, newLines);
        var diffLines = new List<string>
        {
            $"--- a/{edit.RelativePath}",
            $"+++ b/{edit.RelativePath}"
        };
        var hunks = CreateHunks(rows, oldLines.Length, newLines.Length);

        foreach (var hunk in hunks)
        {
            diffLines.Add($"@@ -{hunk.OldStart},{hunk.OldLineCount} +{hunk.NewStart},{hunk.NewLineCount} @@");
            diffLines.AddRange(hunk.Lines);
        }

        var diff = string.Join('\n', diffLines) + "\n";
        if (diff.Length > maxDiffCharacters)
        {
            throw new InvalidOperationException("patch_diff_too_large");
        }

        return new PatchProposalFile(
            edit.RelativePath,
            DetermineKind(edit, oldLines, newLines),
            edit.OriginalContent,
            edit.ProposedContent,
            CodeContextService.ComputeHash(original),
            CodeContextService.ComputeHash(proposed),
            diff,
            hunks);
    }

    private static IReadOnlyList<PatchProposalHunk> CreateHunks(
        IReadOnlyList<DiffRow> rows,
        int oldLineCount,
        int newLineCount)
    {
        if (rows.All(row => row.Kind == ' '))
        {
            return [];
        }

        var ranges = new List<(int Start, int End)>();
        var activeStart = -1;
        var activeEnd = -1;
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Kind == ' ')
            {
                continue;
            }

            var start = Math.Max(0, index - ContextLineCount);
            var end = Math.Min(rows.Count - 1, index + ContextLineCount);
            if (activeStart < 0)
            {
                activeStart = start;
                activeEnd = end;
            }
            else if (start <= activeEnd + 1)
            {
                activeEnd = Math.Max(activeEnd, end);
            }
            else
            {
                ranges.Add((activeStart, activeEnd));
                activeStart = start;
                activeEnd = end;
            }
        }

        if (activeStart >= 0)
        {
            ranges.Add((activeStart, activeEnd));
        }

        return ranges
            .Select(range => CreateHunk(rows, range.Start, range.End, oldLineCount, newLineCount))
            .ToArray();
    }

    private static PatchProposalHunk CreateHunk(
        IReadOnlyList<DiffRow> rows,
        int start,
        int end,
        int oldLineCount,
        int newLineCount)
    {
        var oldBefore = rows.Take(start).Count(row => row.Kind != '+');
        var newBefore = rows.Take(start).Count(row => row.Kind != '-');
        var hunkRows = rows.Skip(start).Take(end - start + 1).ToArray();
        var oldCount = hunkRows.Count(row => row.Kind != '+');
        var newCount = hunkRows.Count(row => row.Kind != '-');

        return new PatchProposalHunk(
            oldCount == 0 && oldLineCount == 0 ? 0 : oldBefore + 1,
            oldCount,
            newCount == 0 && newLineCount == 0 ? 0 : newBefore + 1,
            newCount,
            hunkRows.Select(row => row.Kind + row.Text).ToArray());
    }

    private static IReadOnlyList<DiffRow> BuildRows(
        IReadOnlyList<string> oldLines,
        IReadOnlyList<string> newLines)
    {
        var lcs = new int[oldLines.Count + 1, newLines.Count + 1];
        for (var i = oldLines.Count - 1; i >= 0; i--)
        {
            for (var j = newLines.Count - 1; j >= 0; j--)
            {
                lcs[i, j] = oldLines[i] == newLines[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var rows = new List<DiffRow>();
        var oldIndex = 0;
        var newIndex = 0;
        while (oldIndex < oldLines.Count && newIndex < newLines.Count)
        {
            if (oldLines[oldIndex] == newLines[newIndex])
            {
                rows.Add(new DiffRow(' ', oldLines[oldIndex]));
                oldIndex++;
                newIndex++;
            }
            else if (lcs[oldIndex + 1, newIndex] >= lcs[oldIndex, newIndex + 1])
            {
                rows.Add(new DiffRow('-', oldLines[oldIndex++]));
            }
            else
            {
                rows.Add(new DiffRow('+', newLines[newIndex++]));
            }
        }

        while (oldIndex < oldLines.Count)
        {
            rows.Add(new DiffRow('-', oldLines[oldIndex++]));
        }

        while (newIndex < newLines.Count)
        {
            rows.Add(new DiffRow('+', newLines[newIndex++]));
        }

        return rows;
    }

    private static PatchProposalFileChangeKind DetermineKind(
        PatchProposalFileEdit edit,
        IReadOnlyList<string> oldLines,
        IReadOnlyList<string> newLines)
    {
        if (edit.ChangeKind != PatchProposalFileChangeKind.Modify)
        {
            return edit.ChangeKind;
        }

        if (oldLines.SequenceEqual(newLines))
        {
            return PatchProposalFileChangeKind.NoOp;
        }

        return PatchProposalFileChangeKind.Modify;
    }

    private static string[] SplitLines(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (normalized.Length == 0)
        {
            return [];
        }

        if (normalized.EndsWith('\n'))
        {
            normalized = normalized[..^1];
        }

        return normalized.Split('\n');
    }

    private static bool ContainsBinary(string value) =>
        value.Contains('\0');

    private static void ValidatePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Contains("..", StringComparison.Ordinal) ||
            relativePath.Contains('\\'))
        {
            throw new InvalidOperationException("unsafe_patch_path");
        }
    }

    private sealed record DiffRow(char Kind, string Text);
}
