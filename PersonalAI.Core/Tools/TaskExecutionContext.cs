using PersonalAI.Core.Tasks;

namespace PersonalAI.Core.Tools;

public sealed record TaskExecutionContext(
    TaskId TaskId,
    DateTimeOffset StartedAtUtc);
