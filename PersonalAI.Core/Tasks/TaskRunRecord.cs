namespace PersonalAI.Core.Tasks;

public sealed record TaskRunRecord(
    TaskRun TaskRun,
    IReadOnlyList<TaskEvent> Events,
    IReadOnlyList<TaskArtifact> Artifacts);
