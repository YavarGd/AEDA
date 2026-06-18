namespace PersonalAI.Core.Workers;

public sealed record LocalWorkerDefinition(
    string Id,
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    TimeSpan StartTimeout,
    bool IsEnabled = false);
