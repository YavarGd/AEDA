using PersonalAI.Core.Coding;

namespace PersonalAI.Infrastructure.Coding;

public sealed class CodeChangePlanningService(IValidationPlanService validationPlanService)
    : ICodeChangePlanningService
{
    public Task<CodeChangePlan> CreatePlanAsync(
        CodeChangeRequest request,
        CodeContextPack context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var affected = request.RelativePaths.Count > 0
            ? request.RelativePaths
            : context.Files.Select(file => file.RelativePath).ToArray();
        var sources = context.Files.Select(file => new PatchProposalSource(
            file.WorkspaceId,
            file.RelativePath,
            file.ContentHash,
            "workspace_file")).ToArray();
        var placeholderFiles = affected.Select(path => new PatchProposalFile(
            path,
            PatchProposalFileChangeKind.Modify,
            null,
            null,
            string.Empty,
            string.Empty,
            string.Empty,
            [])).ToArray();

        return Task.FromResult(new CodeChangePlan(
            CreateTitle(request.UserRequest),
            "Review the requested code change and prepare an inspectable patch proposal.",
            affected,
            [
                new CodeChangeStep(
                    1,
                    "Inspect context",
                    "Read the selected workspace files and source-attributed context.",
                    affected),
                new CodeChangeStep(
                    2,
                    "Prepare proposal",
                    "Generate an in-memory unified diff for review.",
                    affected)
            ],
            ["No workspace files will be modified by proposal generation."],
            ["Patch application remains deferred until explicit approval."],
            validationPlanService.CreatePlan(placeholderFiles),
            sources));
    }

    private static string CreateTitle(string text)
    {
        var trimmed = string.IsNullOrWhiteSpace(text) ? "Code change proposal" : text.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }
}
