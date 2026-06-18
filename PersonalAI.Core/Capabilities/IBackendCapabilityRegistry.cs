namespace PersonalAI.Core.Capabilities;

public interface IBackendCapabilityRegistry
{
    BackendCapabilityStatus GetStatus(BackendCapability capability);

    IReadOnlyList<BackendCapabilityStatus> ListStatuses();
}
