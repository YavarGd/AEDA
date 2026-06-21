using PersonalAI.Core.Capabilities;

namespace PersonalAI.Core.Modules;

public readonly record struct AedaModuleId(string Value)
{
    public override string ToString() => Value;

    public static AedaModuleId Chat { get; } = new("chat");

    public static AedaModuleId Code { get; } = new("code");
}

public enum AedaModuleKind
{
    Chat,
    Code,
    Memory,
    Research,
    Claw,
    PicStudio,
    Office,
    Voice,
    Settings
}

public enum AedaModuleStatus
{
    Available,
    PartiallyAvailable,
    Unavailable
}

public enum AedaModuleCapabilityState
{
    Available,
    Unavailable,
    Deferred
}

public sealed record AedaModuleCapability(
    string Id,
    string DisplayName,
    AedaModuleCapabilityState State,
    string? SafeReasonCode = null,
    BackendCapability? BackendCapability = null);

public sealed record AedaModuleRoute(
    string RouteId,
    string? TargetViewModel = null);

public sealed record AedaModuleDescriptor(
    AedaModuleId Id,
    AedaModuleKind Kind,
    string DisplayName,
    string ShortDescription,
    string Glyph,
    AedaModuleStatus Status,
    IReadOnlyList<AedaModuleCapability> Capabilities,
    AedaModuleRoute Route,
    string? SafeUnavailableReason = null,
    int SortOrder = 0);

public sealed record AedaModuleLaunchRequest(
    AedaModuleId ModuleId,
    IReadOnlyDictionary<string, string>? Parameters = null);

public sealed record AedaModuleLaunchResult(
    bool Succeeded,
    AedaModuleRoute? Route,
    string? SafeReasonCode = null);

public sealed record AedaModuleContext(
    AedaModuleId ModuleId,
    DateTimeOffset CreatedAtUtc);

public interface IAedaModule
{
    AedaModuleDescriptor Descriptor { get; }

    Task<AedaModuleLaunchResult> LaunchAsync(
        AedaModuleLaunchRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAedaModuleRegistry
{
    IReadOnlyList<AedaModuleDescriptor> ListModules();

    IReadOnlyList<AedaModuleDescriptor> ListEnabledModules();

    bool TryGetModule(AedaModuleId moduleId, out AedaModuleDescriptor module);

    IReadOnlyList<AedaModuleDescriptor> GetModulesByCapability(string capabilityId);

    AedaModuleStatus GetAvailability(AedaModuleId moduleId);
}
