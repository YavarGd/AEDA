namespace PersonalAI.Core.Chat;

public interface IChatModelCatalog
{
    Task<IReadOnlyList<string>> ListModelsAsync(
        CancellationToken cancellationToken = default);
}

public enum ModelRoutingCategory
{
    General,
    Coding,
    Vision,
    Fast,
    Reasoning
}

public sealed record ModelRoutingAssignment(
    ModelRoutingCategory Category,
    string Model);

public sealed record ModelRoutingRequest(
    string UserPrompt,
    IReadOnlyList<AttachedContextSignal> AttachedContexts,
    IReadOnlyList<string> InstalledModels,
    IReadOnlyList<ModelRoutingAssignment> Assignments);

public sealed record AttachedContextSignal(
    string Type,
    bool HasImage);

public sealed record ModelRoutingDecision(
    string SelectedModel,
    ModelRoutingCategory Category,
    string UserVisibleReason,
    bool ExplicitOverrideHonored,
    string? FallbackReason,
    string RoutedPrompt);

public interface IChatModelRouter
{
    Task<ModelRoutingDecision> SelectModelAsync(
        ModelRoutingRequest request,
        CancellationToken cancellationToken = default);
}
