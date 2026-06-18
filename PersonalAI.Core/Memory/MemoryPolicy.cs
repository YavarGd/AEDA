using PersonalAI.Core.Workspaces;

namespace PersonalAI.Core.Memory;

public sealed record MemoryExclusionRule(
    string Pattern,
    bool IsEnabled = true);

public sealed record MemoryPolicy(
    bool MemoryEnabled,
    bool ExplicitMemoryEnabled,
    bool AutomaticMemoryEnabled,
    bool ProjectMemoryEnabled,
    bool TaskOutcomeMemoryEnabled,
    bool SensitiveMemoryRequiresApproval,
    bool LocalOnly,
    int RetentionDays,
    bool AllowSourceText,
    IReadOnlyList<MemoryExclusionRule> ExclusionRules)
{
    public static MemoryPolicy Default { get; } = new(
        MemoryEnabled: true,
        ExplicitMemoryEnabled: true,
        AutomaticMemoryEnabled: false,
        ProjectMemoryEnabled: true,
        TaskOutcomeMemoryEnabled: true,
        SensitiveMemoryRequiresApproval: true,
        LocalOnly: true,
        RetentionDays: 365,
        AllowSourceText: true,
        ExclusionRules: []);
}

public enum MemoryWriteDecisionKind
{
    Allowed,
    Denied,
    RequiresApproval
}

public sealed record MemoryWriteDecision(
    MemoryWriteDecisionKind Kind,
    string? SafeReasonCode = null)
{
    public static MemoryWriteDecision Allowed { get; } =
        new(MemoryWriteDecisionKind.Allowed);

    public bool IsAllowed => Kind == MemoryWriteDecisionKind.Allowed;
}

public sealed record MemoryRetentionDecision(
    DateTimeOffset? ExpiresAtUtc,
    bool ShouldArchive);

public interface IMemoryPolicyEvaluator
{
    MemoryWriteDecision CanWrite(
        MemoryPolicy policy,
        MemoryKind kind,
        MemoryScope scope,
        MemorySensitivity sensitivity,
        bool isExplicit,
        WorkspaceId? workspaceId = null,
        string? projectId = null);

    MemoryRetentionDecision DecideRetention(
        MemoryPolicy policy,
        DateTimeOffset createdAtUtc);
}

public sealed class MemoryPolicyEvaluator : IMemoryPolicyEvaluator
{
    public MemoryWriteDecision CanWrite(
        MemoryPolicy policy,
        MemoryKind kind,
        MemoryScope scope,
        MemorySensitivity sensitivity,
        bool isExplicit,
        WorkspaceId? workspaceId = null,
        string? projectId = null)
    {
        if (!policy.MemoryEnabled)
        {
            return new MemoryWriteDecision(
                MemoryWriteDecisionKind.Denied,
                "memory_disabled");
        }

        if (!isExplicit && !policy.AutomaticMemoryEnabled)
        {
            return new MemoryWriteDecision(
                MemoryWriteDecisionKind.Denied,
                "automatic_memory_disabled");
        }

        if (isExplicit && !policy.ExplicitMemoryEnabled)
        {
            return new MemoryWriteDecision(
                MemoryWriteDecisionKind.Denied,
                "explicit_memory_disabled");
        }

        if (scope == MemoryScope.Project && !policy.ProjectMemoryEnabled)
        {
            return new MemoryWriteDecision(
                MemoryWriteDecisionKind.Denied,
                "project_memory_disabled");
        }

        if (kind == MemoryKind.TaskOutcome && !policy.TaskOutcomeMemoryEnabled)
        {
            return new MemoryWriteDecision(
                MemoryWriteDecisionKind.Denied,
                "task_outcome_memory_disabled");
        }

        if (IsExcluded(policy, workspaceId?.ToString()) || IsExcluded(policy, projectId))
        {
            return new MemoryWriteDecision(
                MemoryWriteDecisionKind.Denied,
                "memory_scope_excluded");
        }

        if (sensitivity != MemorySensitivity.Normal &&
            policy.SensitiveMemoryRequiresApproval)
        {
            return new MemoryWriteDecision(
                MemoryWriteDecisionKind.RequiresApproval,
                "sensitive_memory_requires_approval");
        }

        return MemoryWriteDecision.Allowed;
    }

    public MemoryRetentionDecision DecideRetention(
        MemoryPolicy policy,
        DateTimeOffset createdAtUtc)
    {
        var days = Math.Clamp(policy.RetentionDays, 1, 3650);
        return new MemoryRetentionDecision(
            createdAtUtc.ToUniversalTime().AddDays(days),
            ShouldArchive: true);
    }

    private static bool IsExcluded(MemoryPolicy policy, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return policy.ExclusionRules.Any(rule =>
            rule.IsEnabled &&
            !string.IsNullOrWhiteSpace(rule.Pattern) &&
            value.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase));
    }
}
