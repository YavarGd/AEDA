using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Coding;

internal static class PatchFileBaseline
{
    public const int MaxBaselineCharacters = 500_000;

    public static WorkspaceTextFile ReadCurrentText(
        IWorkspaceReader workspaceReader,
        Core.Workspaces.WorkspaceId workspaceId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var current = workspaceReader.ReadTextFile(
            workspaceId,
            relativePath,
            MaxBaselineCharacters,
            cancellationToken);
        if (current.IsTruncated || current.HadDecodingErrors)
        {
            throw new InvalidOperationException("proposal_baseline_invalid");
        }

        return current;
    }
}
