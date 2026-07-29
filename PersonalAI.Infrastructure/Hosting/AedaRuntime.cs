using PersonalAI.Core.Approvals;
using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Permissions;
using PersonalAI.Core.Providers;
using PersonalAI.Core.Research;
using PersonalAI.Core.Settings;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Reference;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Chat;
using PersonalAI.Infrastructure.Coding;
using PersonalAI.Infrastructure.Ipc;
using PersonalAI.Infrastructure.Memory;
using PersonalAI.Infrastructure.Modules;
using PersonalAI.Infrastructure.Persistence;
using PersonalAI.Infrastructure.Research;
using PersonalAI.Infrastructure.Settings;
using PersonalAI.Infrastructure.Tasks;
using PersonalAI.Infrastructure.Tools;
using PersonalAI.Infrastructure.Tools.Workspace;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Infrastructure.Hosting;

public sealed class AedaRuntime : IAsyncDisposable
{
    private readonly ProviderFactory _providerFactory;

    private AedaRuntime(
        ProviderFactory providerFactory,
        IApplicationSettingsService settings,
        ChatSessionService chatSession,
        ConversationSessionService conversationSession,
        IAedaModuleRegistry moduleRegistry,
        IAedaCodeModuleService codeModule,
        IAedaMemoryModuleService memoryModule,
        IAedaResearchModuleService researchModule,
        IAedaTaskCenterService taskCenter,
        IWorkspaceRegistrationService workspaceRegistration,
        IWorkspaceRegistry workspaceRegistry,
        ITypedToolRuntime toolRuntime,
        ITaskEventBus taskEventBus,
        ITaskRuntime taskRuntime,
        ITaskQueryService taskQueryService,
        IApprovalCheckpointStore approvalCheckpointStore)
    {
        _providerFactory = providerFactory;
        Settings = settings;
        EditorResponder = new EditorCodeChatResponder(
            chatSession,
            settings,
            ListCurrentModelsAsync);
        ConversationSession = conversationSession;
        ModuleRegistry = moduleRegistry;
        CodeModule = codeModule;
        MemoryModule = memoryModule;
        ResearchModule = researchModule;
        TaskCenter = taskCenter;
        WorkspaceRegistration = workspaceRegistration;
        WorkspaceRegistry = workspaceRegistry;
        ToolRuntime = toolRuntime;
        TaskEventBus = taskEventBus;
        TaskRuntime = taskRuntime;
        TaskQueryService = taskQueryService;
        ApprovalCheckpointStore = approvalCheckpointStore;
    }

    public IApplicationSettingsService Settings { get; }

    public ConversationSessionService ConversationSession { get; }

    public IAedaModuleRegistry ModuleRegistry { get; }

    public IAedaCodeModuleService CodeModule { get; }

    public IAedaMemoryModuleService MemoryModule { get; }

    public IAedaResearchModuleService ResearchModule { get; }

    public IAedaTaskCenterService TaskCenter { get; }

    public IWorkspaceRegistrationService WorkspaceRegistration { get; }

    public EditorCodeChatResponder EditorResponder { get; }

    public IWorkspaceRegistry WorkspaceRegistry { get; }

    public ITypedToolRuntime ToolRuntime { get; }

    public ITaskEventBus TaskEventBus { get; }

    public ITaskRuntime TaskRuntime { get; }

    public ITaskQueryService TaskQueryService { get; }

    public IApprovalCheckpointStore ApprovalCheckpointStore { get; }

    public static async Task<AedaRuntime> CreateAsync(
        IPermissionBroker permissionBroker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissionBroker);

        var conversationRepository = ConversationRepositoryFactory.CreateDefaultRepository();
        await conversationRepository.InitializeAsync(cancellationToken);
        var databasePath = ConversationDatabasePaths.GetDefaultDatabasePath();
        var taskEventStore = new SqliteTaskEventStore(databasePath);
        await taskEventStore.InitializeAsync(cancellationToken);
        var settings = new JsonApplicationSettingsService();
        await settings.InitializeAsync(cancellationToken);
        var providerFactory = new ProviderFactory(secretStore: new DpapiSecretStore());
        var providerCatalog = providerFactory.CreateCatalog(settings.Current);
        var chatSession = new ChatSessionService(providerFactory, settings);
        var taskEventBus = new DurableTaskEventBus(
            new TaskEventBus(),
            taskEventStore);
        var taskRuntime = new TaskRuntime(taskEventStore, taskEventBus);
        var taskQueryService = new TaskQueryService(taskEventStore);
        var approvalCheckpointStore = new InMemoryApprovalCheckpointStore();
        var taskCenter = new AedaTaskCenterService(
            taskQueryService,
            taskRuntime,
            approvalCheckpointStore);
        var toolRegistry = new TypedToolRegistry();
        toolRegistry.Register(new GetCurrentUtcTimeTool());
        IWorkspaceRegistry workspaceRegistry = new WorkspaceRegistry();
        var workspaceOptions = new WorkspaceToolOptions();
        var workspaceResolver = new WorkspacePathResolver(workspaceRegistry);
        var workspaceReader = new FileSystemWorkspaceReader(
            workspaceRegistry,
            workspaceResolver,
            workspaceOptions);
        toolRegistry.Register(new GetWorkspaceInfoTool(
            workspaceReader,
            workspaceResolver,
            workspaceOptions));
        toolRegistry.Register(new ListDirectoryTool(
            workspaceReader,
            workspaceResolver,
            workspaceOptions));
        toolRegistry.Register(new ReadTextFileTool(
            workspaceReader,
            workspaceResolver,
            workspaceOptions));
        toolRegistry.Register(new SearchWorkspaceTextTool(
            workspaceReader,
            workspaceResolver,
            workspaceOptions));
        var patchProposalRepository = new SqlitePatchProposalRepository(databasePath);
        await patchProposalRepository.InitializeAsync(cancellationToken);
        var patchApplyRepository = new SqlitePatchApplyRepository(databasePath);
        await patchApplyRepository.InitializeAsync(cancellationToken);
        var validationRunRepository = new SqliteValidationRunRepository(databasePath);
        await validationRunRepository.InitializeAsync(cancellationToken);
        var memoryRepository = new SqliteMemoryRepository(databasePath);
        await memoryRepository.InitializeAsync(cancellationToken);
        var knowledgeRepository = new SqliteKnowledgeRepository(databasePath);
        await knowledgeRepository.InitializeAsync(cancellationToken);
        var memoryPolicy = new MemoryPolicy(
            settings.Current.MemoryRag.MemoryEnabled,
            settings.Current.MemoryRag.ExplicitMemoryEnabled,
            settings.Current.MemoryRag.AutomaticMemoryEnabled,
            settings.Current.MemoryRag.ProjectMemoryEnabled,
            settings.Current.MemoryRag.TaskOutcomeMemoryEnabled,
            settings.Current.MemoryRag.SensitiveMemoryRequiresApproval,
            settings.Current.MemoryRag.LocalOnlyMemoryMode,
            settings.Current.MemoryRag.RetentionDays,
            AllowSourceText: true,
            ExclusionRules: []);
        var memoryService = new MemoryService(
            memoryRepository,
            new MemoryPolicyEvaluator(),
            memoryPolicy);
        var toolRuntime = new TypedToolRuntime(
            toolRegistry,
            taskEventBus,
            permissionBroker,
            approvalCheckpointStore: approvalCheckpointStore);
        var conversationSession = new ConversationSessionService(
            conversationRepository,
            chatSession,
            toolRegistry,
            toolRuntime,
            workspaceRegistry,
            taskRuntime);
        var codeContextService = new CodeContextService(workspaceReader);
        var validationPlanService = new ValidationPlanService();
        var codeProposalDraftService = new CodeProposalDraftService(
            new LocalFirstModelRoutingPolicy(providerCatalog.Registry),
            new ContextPrivacyFilter(),
            providerCatalog.ChatProviders,
            () => settings.Current.ProviderRouting);
        var patchProposalService = new PatchProposalService(
            patchProposalRepository,
            new UnifiedDiffBuilder(),
            new PatchRiskClassifier(),
            validationPlanService,
            workspaceReader,
            approvalCheckpointStore,
            taskRuntime);
        var patchApplyService = new PatchApplyService(
            patchProposalRepository,
            patchApplyRepository,
            new PatchApplyValidator(
                patchProposalRepository,
                workspaceReader,
                workspaceResolver),
            workspaceReader,
            approvalCheckpointStore,
            taskRuntime);
        var validationCommandAllowlist = new ValidationCommandAllowlist();
        var validationRunnerService = new ValidationRunnerService(
            validationRunRepository,
            validationCommandAllowlist,
            new ControlledProcessRunner(),
            workspaceReader,
            approvalCheckpointStore,
            taskRuntime);
        var backendCapabilities = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: false,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: false,
            hasLocalWorkerSupervisor: false,
            hasStructuredToolRuntime: true,
            hasMemoryRepository: true,
            explicitMemoryEnabled: settings.Current.MemoryRag.ExplicitMemoryEnabled,
            projectMemoryEnabled: settings.Current.MemoryRag.ProjectMemoryEnabled,
            taskOutcomeMemoryEnabled: settings.Current.MemoryRag.TaskOutcomeMemoryEnabled,
            retrievalEnabled: settings.Current.MemoryRag.RagEnabled,
            workspaceIndexingEnabled: settings.Current.MemoryRag.WorkspaceIndexingEnabled,
            hasEmbeddingProvider: false,
            hasVectorIndex: false,
            localOnlyRag: settings.Current.MemoryRag.LocalOnlyMemoryMode,
            hasCodeContextRead: true,
            hasCodeChangePlanning: true,
            hasPatchProposal: true,
            hasPatchReview: true,
            hasPatchApply: true,
            hasPatchRollback: true,
            hasControlledValidation: true,
            hasAedaModules: true,
            hasAedaCodeModule: true,
            hasAedaMemoryModule: true,
            hasAedaResearchModule: true,
            hasModuleDashboard: true,
            hasModuleRouting: true,
            hasCodeTaskTimeline: true,
            hasTaskCenter: true,
            hasActivityTimeline: true,
            hasApprovalInbox: true,
            hasTaskArtifactLinks: true,
            hasModuleTaskSummaries: true);
        var moduleRegistry = new AedaModuleRegistry(
            [
                AedaCodeModuleDescriptorFactory.Create(backendCapabilities),
                AedaTaskCenterModuleDescriptorFactory.Create(backendCapabilities),
                AedaMemoryModuleDescriptorFactory.Create(backendCapabilities),
                AedaResearchModuleDescriptorFactory.Create(backendCapabilities),
                .. AedaDeferredModuleDescriptorFactory.CreateAll()
            ]);
        var retrievalService = new RetrievalService(memoryRepository, knowledgeRepository);
        var researchModule = new AedaResearchModuleService(
            new DeterministicClaimExtractionService(),
            [
                new LocalRagEvidenceProvider(retrievalService),
                new DisabledExternalSearchEvidenceProvider()
            ],
            new InMemoryVerificationReportRepository(),
            backendCapabilities,
            taskRuntime);
        var memoryModule = new AedaMemoryModuleService(
            memoryRepository,
            memoryService,
            backendCapabilities,
            memoryPolicy,
            knowledgeRepository,
            retrievalService);
        var codeModule = new AedaCodeModuleService(
            workspaceReader,
            codeContextService,
            new CodeChangePlanningService(validationPlanService),
            codeProposalDraftService,
            patchProposalService,
            patchApplyService,
            validationRunnerService,
            validationCommandAllowlist,
            taskQueryService,
            taskRuntime);
        var workspaceRegistration = new WorkspaceRegistrationService(
            WorkspaceRepositoryFactory.CreateDefaultRepository(),
            workspaceRegistry,
            toolRuntime);
        await workspaceRegistration.InitializeAsync(cancellationToken);
        return new AedaRuntime(
            providerFactory,
            settings,
            chatSession,
            conversationSession,
            moduleRegistry,
            codeModule,
            memoryModule,
            researchModule,
            taskCenter,
            workspaceRegistration,
            workspaceRegistry,
            toolRuntime,
            taskEventBus,
            taskRuntime,
            taskQueryService,
            approvalCheckpointStore);
    }

    public async Task<IReadOnlyList<string>> ListCurrentModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var catalog = _providerFactory.CreateCatalog(Settings.Current);
        var providerId = new ProviderId(
            Settings.Current.ProviderRouting.SelectedChatProvider);
        return catalog.ChatProviders.TryGetValue(providerId, out var provider) &&
            provider is PersonalAI.Core.Chat.IChatModelCatalog modelCatalog
                ? await modelCatalog.ListModelsAsync(cancellationToken)
                : [];
    }

    public async Task<ProviderHealth> CheckCurrentProviderAsync(
        CancellationToken cancellationToken = default)
    {
        var catalog = _providerFactory.CreateCatalog(Settings.Current);
        return await catalog.Registry.GetHealthAsync(
            new ProviderId(Settings.Current.ProviderRouting.SelectedChatProvider),
            cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
