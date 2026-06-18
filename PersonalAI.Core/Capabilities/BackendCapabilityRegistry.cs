namespace PersonalAI.Core.Capabilities;

public sealed class BackendCapabilityRegistry : IBackendCapabilityRegistry
{
    private readonly IReadOnlyDictionary<BackendCapability, BackendCapabilityStatus> _statuses;

    public BackendCapabilityRegistry(
        IEnumerable<BackendCapabilityStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        _statuses = statuses
            .GroupBy(status => status.Capability)
            .ToDictionary(
                group => group.Key,
                group => group.Last());
    }

    public BackendCapabilityStatus GetStatus(BackendCapability capability) =>
        _statuses.TryGetValue(capability, out var status)
            ? status
            : new BackendCapabilityStatus(
                capability,
                IsAvailable: false,
                SafeReasonCode: "capability_unavailable");

    public IReadOnlyList<BackendCapabilityStatus> ListStatuses() =>
        Enum.GetValues<BackendCapability>()
            .Select(GetStatus)
            .ToArray();

    public static BackendCapabilityRegistry CreateDefault(
        bool hasTaskRuntime,
        bool hasDurableTaskHistory,
        bool hasWorkflowManifestLoader,
        bool hasSpeechToTextProvider,
        bool hasTextToSpeechProvider,
        bool hasLocalWorkerSupervisor,
        bool hasStructuredToolRuntime,
        bool hasAudioCaptureService = false,
        bool hasAudioPlaybackService = false)
    {
        var hasVoiceInput = hasSpeechToTextProvider && hasAudioCaptureService;
        var hasVoiceOutput = hasTextToSpeechProvider && hasAudioPlaybackService;

        return new BackendCapabilityRegistry(
        [
            new(BackendCapability.Chat, true),
            new(BackendCapability.StructuredToolCalls, hasStructuredToolRuntime,
                hasStructuredToolRuntime ? null : "tool_runtime_unavailable"),
            new(BackendCapability.WorkspaceRead, hasStructuredToolRuntime,
                hasStructuredToolRuntime ? null : "workspace_read_unavailable"),
            new(BackendCapability.TaskRuntime, hasTaskRuntime,
                hasTaskRuntime ? null : "task_runtime_unavailable"),
            new(BackendCapability.DurableTaskHistory, hasDurableTaskHistory,
                hasDurableTaskHistory ? null : "durable_task_history_unavailable"),
            new(BackendCapability.WorkflowManifests, hasWorkflowManifestLoader,
                hasWorkflowManifestLoader ? null : "workflow_manifests_unavailable"),
            new(BackendCapability.VoiceInput, hasVoiceInput,
                hasVoiceInput ? null : "voice_input_unavailable"),
            new(BackendCapability.VoiceOutput, hasVoiceOutput,
                hasVoiceOutput ? null : "voice_output_unavailable"),
            new(BackendCapability.PushToTalk, hasVoiceInput,
                hasVoiceInput ? null : "push_to_talk_unavailable"),
            new(BackendCapability.SpeakResponse, hasVoiceOutput,
                hasVoiceOutput ? null : "speak_response_unavailable"),
            new(BackendCapability.LocalWorkerSupervision, hasLocalWorkerSupervisor,
                hasLocalWorkerSupervisor ? null : "local_worker_supervision_unavailable"),
            new(BackendCapability.Embeddings, false, "embeddings_unavailable"),
            new(BackendCapability.Vision, false, "vision_provider_unavailable"),
            new(BackendCapability.WebResearch, false, "web_research_unavailable"),
            new(BackendCapability.CodePatchProposal, false, "code_patch_proposal_unavailable")
        ]);
    }
}
