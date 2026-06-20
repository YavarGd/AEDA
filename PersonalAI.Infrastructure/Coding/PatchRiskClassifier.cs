using PersonalAI.Core.Coding;

namespace PersonalAI.Infrastructure.Coding;

public sealed class PatchRiskClassifier : IPatchRiskClassifier
{
    public (PatchProposalRisk Risk, IReadOnlyList<string> Reasons) Classify(
        IReadOnlyList<PatchProposalFile> files)
    {
        var reasons = new List<string>();
        var risk = PatchProposalRisk.Low;

        foreach (var file in files)
        {
            if (IsUnsafePath(file.RelativePath))
            {
                reasons.Add("unknown_or_unsafe_path");
                risk = PatchProposalRisk.Blocked;
            }

            if (LooksLikeSecret(file.ProposedContent) || LooksLikeSecret(file.UnifiedDiff))
            {
                reasons.Add("secret_looking_content");
                risk = PatchProposalRisk.Blocked;
            }

            if (file.ChangeKind == PatchProposalFileChangeKind.Delete)
            {
                reasons.Add("deletion_or_destructive_path");
                risk = Max(risk, PatchProposalRisk.High);
            }

            var path = file.RelativePath.ToLowerInvariant();
            if (path.Contains("permission", StringComparison.Ordinal) ||
                path.Contains("security", StringComparison.Ordinal) ||
                path.Contains("provider", StringComparison.Ordinal) ||
                path.Contains("routing", StringComparison.Ordinal) ||
                path.Contains("tools/", StringComparison.Ordinal) ||
                path.Contains("tools\\", StringComparison.Ordinal))
            {
                reasons.Add("touches_security_provider_or_tools");
                risk = Max(risk, PatchProposalRisk.High);
            }

            if (path.Contains("migration", StringComparison.Ordinal) ||
                path.Contains("sqlite", StringComparison.Ordinal) ||
                path.Contains("persistence", StringComparison.Ordinal))
            {
                reasons.Add("touches_persistence_or_migrations");
                risk = Max(risk, PatchProposalRisk.High);
            }

            if (path.EndsWith(".csproj", StringComparison.Ordinal) ||
                path.EndsWith(".slnx", StringComparison.Ordinal) ||
                path.EndsWith(".json", StringComparison.Ordinal) ||
                path.EndsWith(".props", StringComparison.Ordinal))
            {
                reasons.Add("touches_project_or_config");
                risk = Max(risk, PatchProposalRisk.Medium);
            }
        }

        if (files.Count > 10)
        {
            reasons.Add("touches_many_files");
            risk = Max(risk, PatchProposalRisk.High);
        }

        if (files.Sum(file => file.UnifiedDiff.Length) > 50_000)
        {
            reasons.Add("large_patch");
            risk = Max(risk, PatchProposalRisk.Medium);
        }

        if (reasons.Count == 0)
        {
            reasons.Add("small_text_change");
        }

        return (risk, reasons.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static PatchProposalRisk Max(
        PatchProposalRisk current,
        PatchProposalRisk candidate) =>
        current == PatchProposalRisk.Blocked || candidate == PatchProposalRisk.Blocked
            ? PatchProposalRisk.Blocked
            : (PatchProposalRisk)Math.Max((int)current, (int)candidate);

    private static bool IsUnsafePath(string path) =>
        string.IsNullOrWhiteSpace(path) ||
        Path.IsPathRooted(path) ||
        path.Contains("..", StringComparison.Ordinal) ||
        path.Contains('\\');

    private static bool LooksLikeSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("sk-", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase);
    }
}
