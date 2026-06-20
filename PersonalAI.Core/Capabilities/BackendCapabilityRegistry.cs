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
        bool hasAudioPlaybackService = false,
        bool hasMemoryRepository = false,
        bool explicitMemoryEnabled = true,
        bool projectMemoryEnabled = true,
        bool taskOutcomeMemoryEnabled = true,
        bool retrievalEnabled = true,
        bool workspaceIndexingEnabled = false,
        bool hasEmbeddingProvider = false,
        bool hasVectorIndex = false,
        bool localOnlyRag = true,
        bool hasCodeContextRead = false,
        bool hasCodeChangePlanning = false,
        bool hasPatchProposal = false,
        bool hasPatchReview = false,
        bool hasPatchApply = false,
        bool hasPatchRollback = false)
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
            new(BackendCapability.Memory, hasMemoryRepository,
                hasMemoryRepository ? null : "memory_repository_unavailable"),
            new(BackendCapability.ExplicitMemory, hasMemoryRepository && explicitMemoryEnabled,
                hasMemoryRepository && explicitMemoryEnabled ? null : "explicit_memory_unavailable"),
            new(BackendCapability.ProjectMemory, hasMemoryRepository && projectMemoryEnabled,
                hasMemoryRepository && projectMemoryEnabled ? null : "project_memory_unavailable"),
            new(BackendCapability.TaskOutcomeMemory, hasMemoryRepository && taskOutcomeMemoryEnabled,
                hasMemoryRepository && taskOutcomeMemoryEnabled ? null : "task_outcome_memory_unavailable"),
            new(BackendCapability.Retrieval, hasMemoryRepository && retrievalEnabled,
                hasMemoryRepository && retrievalEnabled ? "retrieval_text_search_only" : "retrieval_unavailable"),
            new(BackendCapability.WorkspaceIndexing, hasMemoryRepository && workspaceIndexingEnabled,
                hasMemoryRepository && workspaceIndexingEnabled ? null : "workspace_indexing_disabled"),
            new(BackendCapability.Embeddings, hasEmbeddingProvider,
                hasEmbeddingProvider ? null : "embeddings_unconfigured"),
            new(BackendCapability.VectorSearch, hasVectorIndex && hasEmbeddingProvider,
                hasVectorIndex && hasEmbeddingProvider ? null : "vector_search_unconfigured"),
            new(BackendCapability.SemanticRetrieval, hasMemoryRepository && retrievalEnabled && hasEmbeddingProvider && hasVectorIndex,
                hasMemoryRepository && retrievalEnabled && hasEmbeddingProvider && hasVectorIndex ? null : "semantic_retrieval_unconfigured"),
            new(BackendCapability.LocalOnlyRag, hasMemoryRepository && retrievalEnabled && localOnlyRag,
                hasMemoryRepository && retrievalEnabled && localOnlyRag ? null : "local_only_rag_unavailable"),
            new(BackendCapability.Vision, false, "vision_provider_unavailable"),
            new(BackendCapability.WebResearch, false, "web_research_unavailable"),
            new(BackendCapability.CodeContextRead, hasCodeContextRead,
                hasCodeContextRead ? null : "code_context_read_unavailable"),
            new(BackendCapability.CodeChangePlanning, hasCodeChangePlanning,
                hasCodeChangePlanning ? null : "code_change_planning_unavailable"),
            new(BackendCapability.PatchProposal, hasPatchProposal,
                hasPatchProposal ? null : "patch_proposal_unavailable"),
            new(BackendCapability.PatchReview, hasPatchReview,
                hasPatchReview ? null : "patch_review_unavailable"),
            new(BackendCapability.PatchApply, hasPatchApply,
                hasPatchApply ? null : "patch_apply_unavailable"),
            new(BackendCapability.PatchRollback, hasPatchRollback,
                hasPatchRollback ? null : "patch_rollback_unavailable"),
            new(BackendCapability.TestExecution, false, "test_execution_deferred"),
            new(BackendCapability.GitMutation, false, "git_mutation_deferred"),
            new(BackendCapability.CodePatchProposal, hasPatchProposal,
                hasPatchProposal ? null : "code_patch_proposal_unavailable")
        ]);
    }
}
