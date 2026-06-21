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
        bool hasPatchRollback = false,
        bool hasControlledValidation = false,
        bool hasAedaModules = false,
        bool hasAedaCodeModule = false,
        bool hasAedaMemoryModule = false,
        bool hasAedaResearchModule = false,
        bool hasModuleDashboard = false,
        bool hasModuleRouting = false,
        bool hasCodeTaskTimeline = false)
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
            new(BackendCapability.ControlledValidation, hasControlledValidation,
                hasControlledValidation ? null : "controlled_validation_unavailable"),
            new(BackendCapability.TestExecution, hasControlledValidation,
                hasControlledValidation ? null : "test_execution_deferred"),
            new(BackendCapability.ShellExecution, false, "shell_execution_deferred"),
            new(BackendCapability.GitMutation, false, "git_mutation_deferred"),
            new(BackendCapability.CodePatchProposal, hasPatchProposal,
                hasPatchProposal ? null : "code_patch_proposal_unavailable"),
            new(BackendCapability.AedaModules, hasAedaModules,
                hasAedaModules ? null : "aeda_modules_unavailable"),
            new(BackendCapability.AedaCodeModule, hasAedaCodeModule,
                hasAedaCodeModule ? null : "aeda_code_module_unavailable"),
            new(BackendCapability.AedaMemoryModule, hasAedaMemoryModule,
                hasAedaMemoryModule ? null : "aeda_memory_module_unavailable"),
            new(BackendCapability.AedaResearchModule, hasAedaResearchModule,
                hasAedaResearchModule ? null : "aeda_research_module_unavailable"),
            new(BackendCapability.MemoryDashboard, hasAedaMemoryModule && hasMemoryRepository,
                hasAedaMemoryModule && hasMemoryRepository ? null : "memory_dashboard_unavailable"),
            new(BackendCapability.MemorySearch, hasMemoryRepository,
                hasMemoryRepository ? null : "memory_search_unavailable"),
            new(BackendCapability.MemoryEdit, hasMemoryRepository && explicitMemoryEnabled,
                hasMemoryRepository && explicitMemoryEnabled ? null : "memory_edit_unavailable"),
            new(BackendCapability.MemoryDelete, hasMemoryRepository,
                hasMemoryRepository ? null : "memory_delete_unavailable"),
            new(BackendCapability.MemorySourceAttribution, hasMemoryRepository,
                hasMemoryRepository ? null : "memory_source_attribution_unavailable"),
            new(BackendCapability.KnowledgeDocumentBrowser, hasMemoryRepository,
                hasMemoryRepository ? null : "knowledge_documents_unavailable"),
            new(BackendCapability.RetrievalPreview, hasMemoryRepository && retrievalEnabled,
                hasMemoryRepository && retrievalEnabled ? null : "retrieval_preview_unavailable"),
            new(BackendCapability.ModuleDashboard, hasModuleDashboard,
                hasModuleDashboard ? null : "module_dashboard_unavailable"),
            new(BackendCapability.ModuleRouting, hasModuleRouting,
                hasModuleRouting ? null : "module_routing_unavailable"),
            new(BackendCapability.CodeTaskTimeline, hasCodeTaskTimeline,
                hasCodeTaskTimeline ? null : "code_task_timeline_unavailable"),
            new(BackendCapability.ResearchDashboard, hasAedaResearchModule && hasModuleDashboard,
                hasAedaResearchModule && hasModuleDashboard ? null : "research_dashboard_unavailable"),
            new(BackendCapability.ClaimExtraction, hasAedaResearchModule,
                hasAedaResearchModule ? null : "claim_extraction_unavailable"),
            new(BackendCapability.EvidenceTracking, hasAedaResearchModule,
                hasAedaResearchModule ? null : "evidence_tracking_unavailable"),
            new(BackendCapability.VerificationReport, hasAedaResearchModule,
                hasAedaResearchModule ? null : "verification_report_unavailable"),
            new(BackendCapability.CitationReport, hasAedaResearchModule,
                hasAedaResearchModule ? null : "citation_report_unavailable"),
            new(BackendCapability.LocalEvidenceRetrieval, hasAedaResearchModule && hasMemoryRepository && retrievalEnabled,
                hasAedaResearchModule && hasMemoryRepository && retrievalEnabled ? null : "local_evidence_retrieval_unavailable"),
            new(BackendCapability.AreYouSureReview, hasAedaResearchModule,
                hasAedaResearchModule ? null : "are_you_sure_review_unavailable"),
            new(BackendCapability.ExternalSearchProvider, false, "external_search_unconfigured"),
            new(BackendCapability.BrowserResearchAgent, false, "browser_research_agent_deferred")
        ]);
    }
}
