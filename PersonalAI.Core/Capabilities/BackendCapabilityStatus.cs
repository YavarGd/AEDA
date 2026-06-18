namespace PersonalAI.Core.Capabilities;

public sealed record BackendCapabilityStatus(
    BackendCapability Capability,
    bool IsAvailable,
    string? SafeReasonCode = null);
