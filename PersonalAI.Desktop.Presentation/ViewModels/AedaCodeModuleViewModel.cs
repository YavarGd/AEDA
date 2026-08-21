using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAI.Core.Approvals;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Desktop.Presentation.ViewModels;

public sealed partial class AedaCodeModuleViewModel : ObservableObject
{
    private const int SummaryLimit = 6;
    private const int DetailFileLimit = 20;
    private const int DiffLineLimit = 900;
    private const int DiffCharacterLimit = 60_000;
    private const int ValidationOutputLimit = 3_000;
    private const int MaxSelectedContextFiles = 10;
    private const int MaxSelectedContextCharacters = 120_000;
    public const int MaxProposalRequestCharacters = 4_000;
    public const int MaxProposalTitleCharacters = 120;
    private readonly IAedaCodeModuleService _moduleService;
    private readonly IWorkspaceRegistry _workspaceRegistry;
    private readonly IAedaTaskCenterService _taskCenterService;
    private readonly IApprovalCheckpointStore _approvalStore;

    public AedaCodeModuleViewModel(
        IAedaCodeModuleService moduleService,
        IAedaModuleRegistry moduleRegistry,
        IWorkspaceRegistry workspaceRegistry,
        IAedaTaskCenterService taskCenterService,
        IApprovalCheckpointStore approvalStore)
    {
        _moduleService = moduleService ??
            throw new ArgumentNullException(nameof(moduleService));
        _workspaceRegistry = workspaceRegistry ??
            throw new ArgumentNullException(nameof(workspaceRegistry));
        _taskCenterService = taskCenterService ??
            throw new ArgumentNullException(nameof(taskCenterService));
        _approvalStore = approvalStore ??
            throw new ArgumentNullException(nameof(approvalStore));
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        if (moduleRegistry.TryGetModule(AedaModuleId.Code, out var descriptor))
        {
            Descriptor = descriptor;
            CapabilityBadges = descriptor.Capabilities
                .Where(capability =>
                    capability.State != AedaModuleCapabilityState.Deferred)
                .Take(6)
                .Select(capability => capability.DisplayName)
                .ToArray();
        }
        else
        {
            Descriptor = new AedaModuleDescriptor(
                AedaModuleId.Code,
                AedaModuleKind.Code,
                "AEDA Code",
                "AEDA Code is unavailable.",
                "\uE943",
                AedaModuleStatus.Unavailable,
                [],
                new AedaModuleRoute("aeda-code"),
                "aeda_code_module_unavailable",
                SortOrder: 20);
            CapabilityBadges = [];
        }
    }

    public AedaModuleDescriptor Descriptor { get; }

    public IReadOnlyList<string> CapabilityBadges { get; }

    public ObservableCollection<AedaCodeWorkspaceItem> Workspaces { get; } = [];

    public ObservableCollection<AedaCodeProposalItem> Proposals { get; } = [];

    public ObservableCollection<AedaCodeFileItem> ProposalFiles { get; } = [];

    public ObservableCollection<AedaCodeContextFileCandidate> ContextFileCandidates { get; } = [];

    public ObservableCollection<AedaCodeSelectedContextFile> SelectedContextFiles { get; } = [];

    public ObservableCollection<AedaCodeTargetSnippetCandidate> TargetSnippetCandidates { get; } = [];

    public ObservableCollection<AedaCodeApplyItem> ApplyResults { get; } = [];

    public ObservableCollection<AedaCodeValidationTemplateItem> ValidationTemplates { get; } = [];

    public ObservableCollection<AedaCodeValidationRunItem> ValidationRuns { get; } = [];

    public ObservableCollection<AedaTaskSummary> RecentCodeTasks { get; } = [];

    public ObservableCollection<AedaTaskActivityGroup> SelectedTaskTimeline { get; } = [];

    public ObservableCollection<AedaCodeTimelineGroupItem> CodeTimelineGroups { get; } = [];

    public string DisplayName => Descriptor.DisplayName;

    public string ShortDescription => Descriptor.ShortDescription;

    public string AvailabilityLabel => Descriptor.Status switch
    {
        AedaModuleStatus.Available => "Available",
        AedaModuleStatus.PartiallyAvailable => "Needs setup",
        _ => "Unavailable"
    };

    public string WorkspaceSummary => SelectedWorkspace is null
        ? "Select a registered workspace to start a supervised Code session."
        : $"{SelectedWorkspace.DisplayName} · {SelectedWorkspace.RootSummary} · {SelectedWorkspace.PolicyLabel}";

    public string ProposalCreationWorkspaceText => SelectedWorkspace is null
        ? "No workspace selected."
        : $"Workspace: {SelectedWorkspace.DisplayName} - {SelectedWorkspace.PolicyLabel}";

    public string ProposalCreationSafetyText => "Proposal only. No files changed, no validation run, no apply.";

    public string ProposalCreationStateText
    {
        get
        {
            if (IsCreatingProposal)
            {
                return $"{ProposalCreationPhaseLabel}. No files are changing.";
            }

            if (ProposalCreationFailure is not null)
            {
                return $"Failed safely: {ProposalCreationFailure.SafeCode}. Retry is available.";
            }

            return SelectedProposal is null
                ? "Idle. Enter one focused request to create a proposal."
                : "Proposal ready for review. No files changed.";
        }
    }

    public string ProposalRequestLengthText => $"{ProposalRequest.Length}/{MaxProposalRequestCharacters}";

    public string ProposalTitleLengthText => $"{ProposalTitle.Length}/{MaxProposalTitleCharacters}";

    public string ContextSearchStatusText => IsSearchingContext
        ? "Searching safe workspace files..."
        : ContextFileCandidates.Count == 0
            ? "No matching safe files shown."
            : $"{ContextFileCandidates.Count} safe candidate file(s)";

    public string SelectedContextSummaryText
    {
        get
        {
            if (SelectedContextFiles.Count == 0)
            {
                return "No files selected. AEDA will use bounded request-derived context.";
            }

            var total = SelectedContextFiles.Sum(file => file.ApproximateCharacters);
            return $"{SelectedContextFiles.Count} file(s) selected - about {total:N0} chars";
        }
    }

    public string SelectedContextWarningText
    {
        get
        {
            if (SelectedContextFiles.Any(file => !file.IsReadable))
            {
                return "One selected file is no longer available. Remove it or refresh context.";
            }

            if (SelectedContextFiles.Sum(file => file.ApproximateCharacters) > MaxSelectedContextCharacters)
            {
                return "Selected files exceed context budget. Remove files or choose smaller ones.";
            }

            if (SelectedContextFiles.Count > MaxSelectedContextFiles)
            {
                return "Too many context files selected.";
            }

            return string.Empty;
        }
    }

    public bool HasContextFileCandidates => ContextFileCandidates.Count > 0;

    public bool HasSelectedContextFiles => SelectedContextFiles.Count > 0;

    public bool HasTargetSnippetCandidates => TargetSnippetCandidates.Count > 0;

    public bool HasSelectedTargetSnippet => SelectedTargetSnippet is not null;

    public string TargetSnippetStatusText => SelectedContextFiles.Count == 0
        ? "Select a context file to see target snippets."
        : TargetSnippetCandidates.Count == 0
            ? "No private method candidates found in the selected file."
            : SelectedTargetSnippet is null
                ? $"{TargetSnippetCandidates.Count} candidate method(s). No method selected; AEDA will ask the model to choose and provide exact originalText."
                : $"{TargetSnippetCandidates.Count} candidate method(s)";

    public string SelectedTargetSnippetText => SelectedTargetSnippet is null
        ? string.Empty
        : $"Selected: {SelectedTargetSnippet.SignaturePreview} in {SelectedTargetSnippet.RelativePath}";

    public bool HasSelectedContextWarning => !string.IsNullOrWhiteSpace(SelectedContextWarningText);

    public bool HasProposalCreationFailure => ProposalCreationFailure is not null;

    public string ProposalCreationFailureText => ProposalCreationFailure is null
        ? string.Empty
        : $"{ProposalCreationFailure.UserMessage} {ProposalCreationFailure.NextStepHint}";

    public string ProposalCreationFailureDetailText => ProposalCreationFailure is null
        ? string.Empty
        : BuildProposalCreationFailureDetail(ProposalCreationFailure);

    public bool IsWorking => IsBusy || IsCreatingProposal || IsSearchingContext;

    public string WorkingIndicatorText
    {
        get
        {
            if (IsCreatingProposal)
            {
                return $"{ProposalCreationPhaseLabel}. No files are changing.";
            }

            if (IsSearchingContext)
            {
                return "Searching context files...";
            }

            return IsBusy
                ? string.IsNullOrWhiteSpace(SafeStatusMessage)
                    ? "Working..."
                    : SafeStatusMessage
                : string.Empty;
        }
    }

    public string ProposalCreationPhaseLabel => ProposalCreationPhase switch
    {
        AedaCodeProposalCreationPhase.PreparingRequest => "Preparing request",
        AedaCodeProposalCreationPhase.LoadingBoundedContext => "Loading bounded context",
        AedaCodeProposalCreationPhase.CallingCodingModel => "Calling coding model",
        AedaCodeProposalCreationPhase.ParsingModelDraft => "Parsing model draft",
        AedaCodeProposalCreationPhase.RetryingStructuredDraft => "Retrying structured draft",
        AedaCodeProposalCreationPhase.ValidatingProposal => "Validating proposal",
        AedaCodeProposalCreationPhase.SavingProposal => "Saving proposal",
        AedaCodeProposalCreationPhase.Succeeded => "Proposal created",
        AedaCodeProposalCreationPhase.Failed => "Proposal creation failed safely",
        AedaCodeProposalCreationPhase.Cancelled => "Proposal creation cancelled",
        _ => "Idle"
    };

    public string ProposalCreationProgressText
    {
        get
        {
            var parts = new List<string> { $"Phase: {ProposalCreationPhaseLabel}" };
            if (ProposalCreationContextFileCount is > 0)
            {
                parts.Add($"Context files: {ProposalCreationContextFileCount}");
            }

            if (ProposalCreationRetryAttempted)
            {
                parts.Add("Structured retry: yes");
            }

            if (!string.IsNullOrWhiteSpace(ProposalCreationSchemaIssueCode))
            {
                parts.Add($"Schema issue: {ProposalCreationSchemaIssueCode}");
            }

            return string.Join(" | ", parts);
        }
    }

    public string SessionStatusText => Session is null
        ? "No active Code session"
        : $"{Session.Status} · {Session.SafeSummary}";

    public string RecentCodeTaskCountText => $"{RecentCodeTasks.Count} recent Code task(s)";

    public string ProposalCountText => $"{Proposals.Count} proposal(s)";

    public string AffectedFileCountText => SelectedProposalDetail is null
        ? "No files selected"
        : $"{ProposalFiles.Count} affected file(s)";

    public string RiskSummary => SelectedProposal is null
        ? "No proposal selected"
        : $"{SelectedProposal.RiskLabel}: {SelectedProposal.RiskReason}";

    public string SelectedProposalMetadataText => SelectedProposalDetail is null
        ? "Select a proposal to inspect status, risk, affected files, context, and diff."
        : $"Status: {SelectedProposalDetail.Status} | Risk: {SelectedProposalDetail.Risk} | Files: {SelectedProposalDetail.Files.Count}";

    public string SelectedProposalSummaryText => SelectedProposalDetail is null
        ? "No proposal selected yet."
        : RedactSensitiveText(RemoveAbsolutePaths(SelectedProposalDetail.Summary));

    public string SelectedProposalTimestampText => SelectedProposalDetail is null
        ? string.Empty
        : $"Created {SelectedProposalDetail.CreatedAtUtc.ToLocalTime():g} | Updated {SelectedProposalDetail.UpdatedAtUtc.ToLocalTime():g}";

    public string ValidationPlanText { get; private set; } = "Select a proposal to view validation guidance.";

    public string HashStatusText { get; private set; } = "No proposal selected.";

    public string SourceSummaryText { get; private set; } = "No source context loaded.";

    public string DryRunStatusText => DryRunPlan is null
        ? "Dry run not started."
        : $"{DryRunPlan.Status} · {DryRunPlan.Operations.Count} operation(s)";

    public string ReviewGateOrderText =>
        "1. Dry run selected proposal\n2. Request apply approval\n3. Apply approved proposal";

    public string DryRunDetailText => DryRunPlan is null
        ? "Dry run checks patch safety and stale hashes before any write."
        : IsDryRunStale
            ? "This proposal is stale. The file changed since the proposal was created, so AEDA blocked apply. Create a fresh proposal from the current file state. Safe code: StaleOriginalContent."
        : DryRunPlan.Status == PatchApplyStatus.DryRunPassed
            ? $"Dry run passed for {DryRunPlan.Operations.Count} file operation(s)."
            : $"Dry run blocked: {FormatReasons(DryRunPlan.FailureReasons)}";

    public bool IsDryRunStale =>
        DryRunPlan?.FailureReasons.Contains(PatchApplyFailureReason.StaleOriginalContent) == true;

    public string StaleProposalRecoveryText => IsDryRunStale
        ? "Apply is disabled for stale proposals. Re-enter the request to create a fresh proposal; AEDA will not apply or validate the stale one."
        : string.Empty;

    public string ApplyApprovalStatusText => ApplyApprovalRequest is null
        ? "Apply approval not requested."
        : ApplyApprovalDecision is null
            ? "Apply approval requested."
            : ApplyApprovalDecision.IsAllowed
                ? "Apply approval granted once."
                : "Apply approval denied by user.";

    public string ApplyResultText => ApplyResult is null
        ? "No apply result."
        : $"{ApplyResult.Status} · {ApplyResult.Files.Count} file result(s)";

    public string ApplyResultDetailText => ApplyResult is null
        ? "Apply is available only after a passed dry run and granted approval."
        : ApplyResult.Status is PatchApplyStatus.Applied or PatchApplyStatus.PartiallyApplied
            ? HasRollbackAvailable
                ? $"Changed files: {ApplyResult.Files.Count}. Backup checkpoint available. Rollback available."
                : $"Changed files: {ApplyResult.Files.Count}. Rollback unavailable for this apply result."
            : $"Apply did not complete: {FormatReasons(ApplyResult.FailureReasons)}";

    public string ValidationApprovalStatusText => ValidationApprovalRequest is null
        ? "Validation approval not requested."
        : ValidationApprovalDecision is null
            ? "Validation approval requested."
            : ValidationApprovalDecision.IsAllowed
                ? "Validation approval granted once."
                : "Validation approval denied by user.";

    public string ValidationResultText => ValidationRun is null
        ? "No validation run selected."
        : $"{ValidationRun.Status} · {ValidationRun.TemplateId}";

    public string RollbackStatusText => RollbackResult is null
        ? "Rollback not run."
        : $"{RollbackResult.Status} · {RollbackResult.Files.Count} file result(s)";

    public string ValidationTemplateStatusText => HasValidationTemplates
        ? "Only allowlisted validation templates are shown. No arbitrary command input is available."
        : "No allowlisted validation templates are available for this workspace.";

    public string ValidationRunDetailText => ValidationRun is null
        ? "Create a validation run, request approval, then run the approved validation explicitly."
        : BuildValidationRunDetail(ValidationRun);

    public string RollbackAvailabilityText => HasRollbackAvailable
        ? $"Rollback available for {ApplyResult?.Files.Count ?? 0} applied file(s). User-triggered only."
        : "Rollback is hidden until an apply result has rollback capability.";

    public bool HasTimelineItems => CodeTimelineGroups.Count > 0;

    public bool HasNoTimelineItems => !HasTimelineItems;

    public bool HasWorkspaces => Workspaces.Count > 0;

    public bool HasNoWorkspaces => !HasWorkspaces;

    public bool HasProposals => Proposals.Count > 0;

    public bool HasNoProposals => !HasProposals;

    public bool HasProposal => SelectedProposal is not null;

    public bool HasNoSelectedProposal => !HasProposal;

    public bool HasProposalFiles => ProposalFiles.Count > 0;

    public bool HasValidationTemplates => ValidationTemplates.Count > 0;

    public bool HasNoValidationTemplates => !HasValidationTemplates;

    public bool HasValidationRuns => ValidationRuns.Count > 0;

    public bool HasNoValidationRuns => !HasValidationRuns;

    public bool HasApplyResults => ApplyResults.Count > 0;

    public bool HasNoApplyResults => !HasApplyResults;

    public bool HasRecentCodeTasks => RecentCodeTasks.Count > 0;

    public bool HasNoRecentCodeTasks => !HasRecentCodeTasks;

    public bool HasSelectedTaskTimeline => SelectedTaskTimeline.Count > 0;

    public bool HasNoSelectedTaskTimeline => !HasSelectedTaskTimeline;

    public bool HasRollbackAvailable =>
        ApplyResult is { Status: PatchApplyStatus.Applied or PatchApplyStatus.PartiallyApplied } result &&
        SelectedWorkspace is not null &&
        result.WorkspaceId == SelectedWorkspace.WorkspaceId &&
        !_selectedApplyResultAlreadyRolledBack &&
        !(RollbackResult is { Status: PatchApplyStatus.RolledBack } rollback && rollback.ApplyResultId == result.Id) &&
        result.Files.Any(file =>
            file.Status == PatchApplyStatus.Applied &&
            file.ChangeKind is PatchProposalFileChangeKind.Modify or PatchProposalFileChangeKind.Add);

    public bool CanShowRollback => HasRollbackAvailable;

    public IReadOnlyList<AedaCodeSession> RecentSessions { get; private set; } = [];

    public IReadOnlyList<AedaCodeProposalSummary> ProposalSummaries => Proposals
        .Select(item => item.Summary)
        .ToArray();

    public IReadOnlyList<AedaCodeApplySummary> ApplySummaries => ApplyResults
        .Select(item => item.Summary)
        .ToArray();

    public IReadOnlyList<AedaCodeValidationSummary> ValidationSummaries => ValidationRuns
        .Select(item => item.Summary)
        .ToArray();

    public IReadOnlyList<AedaCodeTimelineItem> TimelineSummaries => RecentCodeTasks.Count > 0
        ? RecentCodeTasks
            .Take(SummaryLimit)
            .Select(task => new AedaCodeTimelineItem(
                task.UpdatedAtUtc,
                "task",
                $"{task.Title} ({task.Status.Label})"))
            .ToArray()
        : Dashboard?.Timeline.Take(SummaryLimit).ToArray() ?? [];

    public bool HasRecentSessions => RecentSessions.Count > 0;

    public bool HasNoRecentSessions => !HasRecentSessions;

    public bool HasDashboard => Dashboard is not null;

    public bool HasProposalSummaries => HasProposals;

    public bool HasNoProposalSummaries => HasNoProposals;

    public bool HasValidationSummaries => HasValidationRuns;

    public bool HasNoValidationSummaries => HasNoValidationRuns;

    public bool HasApplySummaries => HasApplyResults;

    public bool HasNoApplySummaries => HasNoApplyResults;

    public bool HasTimelineSummaries => HasRecentCodeTasks;

    public bool HasNoTimelineSummaries => HasNoRecentCodeTasks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkspaceSummary))]
    private AedaCodeWorkspaceItem? _selectedWorkspace;

    /// <summary>
    /// The workspace whose Apply-history/rollback presentation state
    /// (<see cref="ApplyResults"/> etc.) is currently valid, or null if that
    /// state has been cleared and not yet reloaded. Tracked independently of
    /// <see cref="SelectedWorkspace"/> because Avalonia's two-way binding can
    /// set <see cref="SelectedWorkspace"/> before <see cref="SelectWorkspaceAsync"/>
    /// runs, making a same-value comparison against the command's argument
    /// unreliable for detecting a genuine transition.
    /// </summary>
    private WorkspaceId? _applyHistoryWorkspaceId;

    /// <summary>
    /// Set by <see cref="OnSelectedWorkspaceChanging"/> whenever the
    /// workspace identity genuinely transitions, and consumed by
    /// <see cref="SelectWorkspaceAsync"/> to decide whether to run its own
    /// transition-only side effects (context clearing, dashboard reload).
    /// </summary>
    private bool _workspaceTransitionPending;

    /// <summary>
    /// Monotonic counter incremented every time the workspace identity
    /// genuinely transitions (see <see cref="OnSelectedWorkspaceChanging"/>).
    /// Every workspace-scoped asynchronous operation that mutates
    /// presentation state must capture this value (alongside the target
    /// workspace id) before awaiting, and re-check it against the current
    /// value before committing results - a mismatch means the user has since
    /// moved to a different workspace (or back to the same one through a new
    /// transition) and the stale result must not be applied.
    /// </summary>
    private long _workspaceGeneration;

    /// <summary>
    /// Monotonic counter identifying the most recently started
    /// <see cref="SearchContextFilesAsync"/> call, independent of
    /// <see cref="_workspaceGeneration"/>. Two overlapping searches in the
    /// same workspace (no workspace switch involved) don't advance the
    /// workspace generation, so this counter is what lets an earlier
    /// search's <c>finally</c> block tell whether a newer search has since
    /// started and avoid clearing <see cref="IsSearchingContext"/> out from
    /// under it.
    /// </summary>
    private long _contextSearchOperationId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionStatusText))]
    private AedaCodeSession? _session;

    [ObservableProperty]
    private AedaCodeDashboardModel? _dashboard;

    [ObservableProperty]
    private AedaCodeProposalItem? _selectedProposal;

    [ObservableProperty]
    private PatchProposal? _selectedProposalDetail;

    [ObservableProperty]
    private AedaCodeValidationTemplateItem? _selectedValidationTemplate;

    [ObservableProperty]
    private AedaTaskSummary? _selectedTask;

    [ObservableProperty]
    private string _unifiedDiffPreview = "Select a proposal to preview its unified diff.";

    [ObservableProperty]
    private string _validationOutputPreview = "Run an approved validation to view sanitized output.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkingIndicatorText))]
    private string _safeStatusMessage = "AEDA Code ready.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorking))]
    [NotifyPropertyChangedFor(nameof(WorkingIndicatorText))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProposalRequestLengthText))]
    private string _proposalRequest = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProposalTitleLengthText))]
    private string _proposalTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContextSearchStatusText))]
    private string _contextFileSearchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContextSearchStatusText))]
    [NotifyPropertyChangedFor(nameof(IsWorking))]
    [NotifyPropertyChangedFor(nameof(WorkingIndicatorText))]
    private bool _isSearchingContext;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTargetSnippet))]
    [NotifyPropertyChangedFor(nameof(SelectedTargetSnippetText))]
    private AedaCodeTargetSnippetCandidate? _selectedTargetSnippet;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorking))]
    [NotifyPropertyChangedFor(nameof(WorkingIndicatorText))]
    private bool _isCreatingProposal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProposalCreationFailure))]
    [NotifyPropertyChangedFor(nameof(ProposalCreationFailureText))]
    [NotifyPropertyChangedFor(nameof(ProposalCreationFailureDetailText))]
    private AedaCodeProposalCreationFailure? _proposalCreationFailure;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProposalCreationStateText))]
    [NotifyPropertyChangedFor(nameof(ProposalCreationPhaseLabel))]
    [NotifyPropertyChangedFor(nameof(ProposalCreationProgressText))]
    [NotifyPropertyChangedFor(nameof(WorkingIndicatorText))]
    private AedaCodeProposalCreationPhase _proposalCreationPhase = AedaCodeProposalCreationPhase.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProposalCreationProgressText))]
    private int? _proposalCreationContextFileCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProposalCreationProgressText))]
    private bool _proposalCreationRetryAttempted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProposalCreationProgressText))]
    private string? _proposalCreationSchemaIssueCode;

    private CancellationTokenSource? _proposalCreationCancellation;

    private PatchApplyPlan? DryRunPlan { get; set; }

    private ApprovalRequest? ApplyApprovalRequest { get; set; }

    private ApprovalDecision? ApplyApprovalDecision { get; set; }

    private PatchApplyResult? ApplyResult { get; set; }

    private ValidationRun? ValidationRun { get; set; }

    private ApprovalRequest? ValidationApprovalRequest { get; set; }

    private ApprovalDecision? ValidationApprovalDecision { get; set; }

    private PatchRollbackResult? RollbackResult { get; set; }

    private bool _selectedApplyResultAlreadyRolledBack;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            LoadRegisteredWorkspaces();
            RecentSessions = await _moduleService.ListRecentSessionsAsync(
                SummaryLimit,
                cancellationToken);
            await LoadRecentCodeTasksAsync(cancellationToken);
            if (SelectedWorkspace is null)
            {
                SelectedWorkspace = Workspaces.FirstOrDefault();
            }

            SafeStatusMessage = Workspaces.Count == 0
                ? "Register a workspace before starting an AEDA Code session."
                : "AEDA Code workspace list loaded.";
        }
        catch (OperationCanceledException)
        {
            SafeStatusMessage = "AEDA Code refresh cancelled.";
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            SafeStatusMessage = "AEDA Code is temporarily unavailable.";
        }
        finally
        {
            IsBusy = false;
            NotifyAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartSession))]
    public async Task StartSessionAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedWorkspace is null)
        {
            SafeStatusMessage = "Select a registered workspace first.";
            return;
        }

        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;

        try
        {
            IsBusy = true;
            var session = await _moduleService.StartSessionAsync(
                targetWorkspaceId,
                "Supervised Code workflow",
                cancellationToken);

            if (SelectedWorkspace?.WorkspaceId != targetWorkspaceId || _workspaceGeneration != generation)
            {
                // The user switched to a different workspace before this
                // session finished starting. The backend session was created
                // successfully, but it must not be surfaced (reselecting the
                // old workspace or populating its dashboard) under whatever
                // workspace is now current.
                return;
            }

            Session = session;
            await RefreshDashboardAsync(cancellationToken);
            SafeStatusMessage = "AEDA Code session ready.";
        }
        catch (OperationCanceledException)
        {
            SafeStatusMessage = "AEDA Code session start cancelled.";
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            SafeStatusMessage = "Could not start the Code session safely.";
        }
        finally
        {
            IsBusy = false;
            NotifyAll();
        }
    }

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        LoadRegisteredWorkspaces();
        if (Session is not null && Session.WorkspaceId == SelectedWorkspace?.WorkspaceId)
        {
            // The active session belongs to the currently selected
            // workspace: its dashboard is the authoritative source.
            await RefreshDashboardAsync(cancellationToken);
        }
        else if (SelectedWorkspace is not null)
        {
            // Either there is no active session, or it belongs to a
            // different workspace than the one currently selected (e.g. the
            // user switched away after starting a session elsewhere). The
            // currently selected workspace is the presentation authority, so
            // refresh its own workflow instead of applying a mismatched
            // session's dashboard or silently reselecting the session's
            // workspace.
            await LoadWorkspaceWorkflowAsync(cancellationToken);
        }

        await LoadRecentCodeTasksAsync(cancellationToken);
        SafeStatusMessage = "AEDA Code workflow refreshed.";
        NotifyAll();
    }

    public async Task RefreshDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        if (Session is null)
        {
            SafeStatusMessage = "No AEDA Code session.";
            return;
        }

        // Capture the session's own target identity and the current
        // generation before awaiting: the user may switch workspaces (or
        // switch away and back, advancing the generation) while this
        // dashboard is loading.
        var sessionId = Session.Id;
        var targetWorkspaceId = Session.WorkspaceId;
        var generation = _workspaceGeneration;

        var dashboard = await _moduleService.GetDashboardAsync(sessionId, cancellationToken);

        if (SelectedWorkspace?.WorkspaceId != targetWorkspaceId ||
            _workspaceGeneration != generation ||
            dashboard.Workspace.WorkspaceId != targetWorkspaceId)
        {
            // The user has moved to a different workspace since this refresh
            // started (or the session simply doesn't belong to the currently
            // selected workspace to begin with) - a mismatched dashboard
            // must never be applied under, or reselect, another workspace.
            SafeStatusMessage = "AEDA Code session belongs to a different workspace.";
            return;
        }

        Dashboard = dashboard;
        SafeStatusMessage = "AEDA Code dashboard refreshed.";
    }

    [RelayCommand]
    public async Task SelectWorkspaceAsync(
        AedaCodeWorkspaceItem? workspace,
        CancellationToken cancellationToken = default)
    {
        // This assignment is a no-op (and triggers no Changing/Changed
        // notifications) if two-way binding already set SelectedWorkspace to
        // this same workspace before the command ran - the real transition
        // detection and Apply-history clearing already happened in
        // OnSelectedWorkspaceChanging, whenever the identity actually
        // changed, regardless of which caller triggered it.
        SelectedWorkspace = workspace;
        var workspaceChanged = _workspaceTransitionPending;
        _workspaceTransitionPending = false;

        ClearProposalDetail();
        if (workspaceChanged)
        {
            ClearSelectedContext();
            ContextFileCandidates.Clear();
        }

        if (workspace is null)
        {
            SafeStatusMessage = "Select a registered workspace.";
            NotifyAll();
            return;
        }

        await LoadWorkspaceWorkflowAsync(cancellationToken);

        if (Session is { } activeSession &&
            activeSession.WorkspaceId == workspace.WorkspaceId &&
            _applyHistoryWorkspaceId != workspace.WorkspaceId)
        {
            // The active session already belongs to the newly selected
            // workspace (e.g. switching back to a workspace whose session is
            // still live) and its persisted apply history has not already
            // been loaded, so it can be safely reloaded through the existing
            // authoritative dashboard path.
            var targetWorkspaceId = workspace.WorkspaceId;
            var reloadGeneration = _workspaceGeneration;
            var dashboard = await _moduleService.GetDashboardAsync(activeSession.Id, cancellationToken);
            if (SelectedWorkspace?.WorkspaceId == targetWorkspaceId &&
                _workspaceGeneration == reloadGeneration &&
                dashboard.Workspace.WorkspaceId == targetWorkspaceId)
            {
                // Guard against a rapid second workspace switch completing
                // first: only apply this dashboard if the user is still on
                // the workspace (and generation) it was loaded for.
                // ApplyDashboard (invoked by the Dashboard setter) records
                // _applyHistoryWorkspaceId.
                Dashboard = dashboard;
            }
        }

        SafeStatusMessage = "Workspace workflow loaded.";
        NotifyAll();
    }

    [RelayCommand]
    public async Task SelectProposalAsync(
        AedaCodeProposalItem? proposal,
        CancellationToken cancellationToken = default)
    {
        SelectedProposal = proposal;
        ClearActionState();
        ProposalFiles.Clear();
        SelectedProposalDetail = null;

        if (proposal is null)
        {
            ClearProposalDetail();
            NotifyAll();
            return;
        }

        if (SelectedWorkspace is null)
        {
            ClearProposalDetail();
            NotifyAll();
            return;
        }

        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var targetProposalId = proposal.ProposalId;

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId &&
            _workspaceGeneration == generation &&
            SelectedProposal?.ProposalId == targetProposalId;

        try
        {
            var detail = await _moduleService.GetProposalAsync(
                targetProposalId,
                cancellationToken);
            if (!IsCurrentOperation())
            {
                return;
            }

            if (detail is null)
            {
                SafeStatusMessage = "Selected proposal is no longer available.";
                ClearProposalDetail();
                return;
            }

            if (detail.WorkspaceId != targetWorkspaceId)
            {
                return;
            }

            SelectedProposalDetail = detail;
            foreach (var file in detail.Files
                         .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                         .Take(DetailFileLimit))
            {
                ProposalFiles.Add(AedaCodeFileItem.From(file));
            }

            UnifiedDiffPreview = BuildBoundedDiff(detail);
            ValidationPlanText = BuildValidationPlanText(detail.ValidationPlan);
            HashStatusText = BuildHashStatusText(detail);
            SourceSummaryText = BuildSourceSummaryText(detail);
            SelectedValidationTemplate = ValidationTemplates.FirstOrDefault();
            SafeStatusMessage = "Proposal detail loaded.";
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Proposal load cancelled.";
            }
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Proposal detail is temporarily unavailable.";
                ClearProposalDetail();
            }
        }
        finally
        {
            NotifyAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateProposal))]
    public async Task CreateProposalAsync()
    {
        if (SelectedWorkspace is null)
        {
            SafeStatusMessage = "Select a registered workspace first.";
            return;
        }

        _proposalCreationCancellation?.Dispose();
        _proposalCreationCancellation = new CancellationTokenSource();
        var cancellationToken = _proposalCreationCancellation.Token;

        // Capture the target identity before the first await: the user may
        // switch workspaces (or switch away and back, advancing the
        // generation) while this creation is running in the background via
        // Task.Run below. The backend creation is intentionally NOT
        // cancelled on a workspace switch - only an explicit
        // CancelProposalCreation() cancels it - so it must be allowed to run
        // to completion and persist server-side; only its presentation
        // (progress updates and the final result) must be withheld once
        // stale.
        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId && _workspaceGeneration == generation;

        try
        {
            IsBusy = true;
            IsCreatingProposal = true;
            ProposalCreationPhase = AedaCodeProposalCreationPhase.PreparingRequest;
            ProposalCreationContextFileCount = null;
            ProposalCreationRetryAttempted = false;
            ProposalCreationSchemaIssueCode = null;
            ProposalCreationFailure = null;
            SafeStatusMessage = "Creating proposal only. No files will be changed.";
            var request = new AedaCodeProposalCreationRequest(
                targetWorkspaceId,
                ProposalRequest,
                string.IsNullOrWhiteSpace(ProposalTitle) ? null : ProposalTitle,
                SelectedContextFiles.Count == 0
                    ? null
                    : new AedaCodeProposalContextSelection(
                        SelectedContextFiles.Select(file => file.RelativePath).ToArray(),
                        SelectedTargetSnippet is null
                            ? null
                            : new AedaCodeSelectedTargetSnippet(
                                SelectedTargetSnippet.Id,
                                SelectedTargetSnippet.RelativePath)));
            // Progress<T> already marshals each Report call back to this
            // thread (it captures the SynchronizationContext at construction
            // time), so the only thing this wrapper adds is the staleness
            // check - reports for a creation that is no longer current (the
            // user has since switched workspaces) must never reach
            // OnProposalCreationProgress and overwrite the now-current
            // workspace's phase/status text.
            var progress = new Progress<AedaCodeProposalCreationProgress>(update =>
            {
                if (IsCurrentOperation())
                {
                    OnProposalCreationProgress(update);
                }
            });
            var result = await Task.Run(
                () => _moduleService.CreateProposalFromRequestAsync(
                    request,
                    progress,
                    cancellationToken),
                cancellationToken);

            if (!IsCurrentOperation())
            {
                // The user switched workspaces (or transitioned away and
                // back) while this creation was in flight. The proposal was
                // already created and persisted server-side - it will
                // appear the next time this workspace's proposals are
                // legitimately reloaded. It must not be surfaced, and the
                // now-current workspace's own state must not be disturbed,
                // by this stale completion.
                return;
            }

            AddOrUpdateProposal(result.Summary);
            ProposalRequest = string.Empty;
            ProposalTitle = string.Empty;
            ProposalCreationFailure = null;
            ProposalCreationPhase = AedaCodeProposalCreationPhase.Succeeded;
            await SelectProposalAsync(
                Proposals.FirstOrDefault(item => item.ProposalId == result.Proposal.Id),
                cancellationToken);
            await LoadRecentCodeTasksAsync(cancellationToken);
            SafeStatusMessage = "Proposal created for review. No files changed.";
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentOperation())
            {
                ProposalCreationPhase = AedaCodeProposalCreationPhase.Cancelled;
                ProposalCreationFailure = AedaCodeProposalCreationFailure.FromReason(
                    AedaCodeProposalCreationFailureReason.ModelCancelled);
                SafeStatusMessage = "Proposal creation cancelled. No files changed.";
            }
        }
        catch (AedaCodeProposalCreationException exception)
        {
            if (IsCurrentOperation())
            {
                ProposalCreationPhase = exception.Failure.Reason == AedaCodeProposalCreationFailureReason.ModelCancelled
                    ? AedaCodeProposalCreationPhase.Cancelled
                    : AedaCodeProposalCreationPhase.Failed;
                ProposalCreationRetryAttempted = exception.Failure.RetryAttempted;
                ProposalCreationSchemaIssueCode = exception.Failure.SchemaIssueCode;
                ProposalCreationFailure = exception.Failure;
                SafeStatusMessage = exception.Failure.UserMessage;
            }
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            if (IsCurrentOperation())
            {
                ProposalCreationPhase = AedaCodeProposalCreationPhase.Failed;
                ProposalCreationFailure = MapProposalCreationFailure(exception);
                SafeStatusMessage = ProposalCreationFailure.UserMessage;
            }
        }
        finally
        {
            // Nothing else in another workspace ever sets IsCreatingProposal
            // (only this method does), so unconditionally clearing it (and
            // IsBusy, which this method also owns exclusively while it
            // runs) here cannot stomp a legitimate current value - it only
            // ever un-sticks the working indicator.
            IsCreatingProposal = false;
            IsBusy = false;
            _proposalCreationCancellation?.Dispose();
            _proposalCreationCancellation = null;
            NotifyAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSearchContextFiles))]
    public async Task SearchContextFilesAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedWorkspace is null)
        {
            SafeStatusMessage = "Select a registered workspace before searching context files.";
            return;
        }

        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        // A second call to this same command (same workspace, same
        // generation) can be issued before an earlier one has resolved -
        // e.g. the user retypes the query. _workspaceGeneration alone can't
        // distinguish the two since neither changed, so a dedicated counter
        // tracks which call is the newest; only that call's finally block
        // may clear IsSearchingContext.
        var operationId = ++_contextSearchOperationId;
        var request = new AedaCodeContextSearchRequest(
            targetWorkspaceId,
            ContextFileSearchQuery,
            SelectedContextFiles.Select(file => file.RelativePath).ToArray());

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId && _workspaceGeneration == generation;

        try
        {
            IsSearchingContext = true;
            ContextFileCandidates.Clear();
            var result = await _moduleService.SearchContextFilesAsync(request, cancellationToken);
            if (!IsCurrentOperation() ||
                result.WorkspaceId != targetWorkspaceId ||
                result.Candidates.Any(candidate => candidate.WorkspaceId != targetWorkspaceId))
            {
                return;
            }

            foreach (var candidate in result.Candidates)
            {
                ContextFileCandidates.Add(candidate);
            }

            SafeStatusMessage = result.IsTruncated
                ? "Context file results were bounded. Narrow the search if needed."
                : "Context file results loaded.";
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Context file search cancelled.";
            }
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Could not search context files safely.";
            }
        }
        finally
        {
            if (_contextSearchOperationId == operationId)
            {
                // Only clear the indicator if no newer search has started
                // since this one began - otherwise this stale completion
                // would hide the still-running newer search's indicator.
                IsSearchingContext = false;
            }

            NotifyAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddContextFile))]
    public async Task AddContextFileAsync(
        AedaCodeContextFileCandidate? candidate,
        CancellationToken cancellationToken = default)
    {
        if (SelectedWorkspace is null ||
            candidate is null ||
            candidate.WorkspaceId != SelectedWorkspace.WorkspaceId)
        {
            return;
        }

        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var targetRelativePath = candidate.RelativePath;

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId && _workspaceGeneration == generation;

        if (SelectedContextFiles.Any(file => string.Equals(
                file.RelativePath,
                candidate.RelativePath,
                StringComparison.OrdinalIgnoreCase)))
        {
            SafeStatusMessage = "Context file is already selected.";
            return;
        }

        if (SelectedContextFiles.Count >= MaxSelectedContextFiles)
        {
            SafeStatusMessage = "Selected context file limit reached.";
            return;
        }

        try
        {
            var pack = await _moduleService.ReadFilesAsync(
                targetWorkspaceId,
                [targetRelativePath],
                cancellationToken);
            if (!IsCurrentOperation() ||
                pack.WorkspaceId != targetWorkspaceId ||
                pack.Files.Any(file => file.WorkspaceId != targetWorkspaceId))
            {
                return;
            }

            var file = pack.Files.SingleOrDefault();
            if (file is null ||
                !string.Equals(file.RelativePath, targetRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                SafeStatusMessage = "One selected file is no longer available. Remove it or refresh context.";
                return;
            }

            SelectedContextFiles.Add(new AedaCodeSelectedContextFile(
                file.RelativePath,
                Path.GetFileName(file.RelativePath),
                GetContainingFolder(file.RelativePath),
                Path.GetExtension(file.RelativePath),
                file.IsTruncated
                    ? $"{FormatSize(file.FileSizeBytes)} - truncated"
                    : FormatSize(file.FileSizeBytes),
                file.FileSizeBytes,
                IsReadable: true,
                file.IsTruncated,
                file.Content.Length,
                file.IsTruncated ? "file_truncated" : null));
            await RefreshTargetSnippetCandidatesAsync(cancellationToken);
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Context file selected. No files changed.";
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Context file add cancelled.";
            }
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "One selected file is no longer available. Remove it or refresh context.";
            }
        }
        finally
        {
            if (IsCurrentOperation())
            {
                RefreshContextCandidateSelectionState();
            }

            NotifyAll();
        }
    }

    [RelayCommand]
    public void RemoveContextFile(AedaCodeSelectedContextFile? file)
    {
        if (file is null)
        {
            return;
        }

        var existing = SelectedContextFiles.FirstOrDefault(item => string.Equals(
            item.RelativePath,
            file.RelativePath,
            StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedContextFiles.Remove(existing);
            RemoveTargetSnippetsForFile(existing.RelativePath);
            RefreshContextCandidateSelectionState();
            SafeStatusMessage = "Context file removed.";
            NotifyAll();
        }
    }

    [RelayCommand]
    public void ClearSelectedContext()
    {
        SelectedContextFiles.Clear();
        TargetSnippetCandidates.Clear();
        SelectedTargetSnippet = null;
        RefreshContextCandidateSelectionState();
        SafeStatusMessage = "Selected context cleared.";
        NotifyAll();
    }

    [RelayCommand]
    public void SelectTargetSnippet(AedaCodeTargetSnippetCandidate? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        SelectedTargetSnippet = candidate;
        SafeStatusMessage = "Target snippet selected. Proposal only; no files changed.";
        NotifyAll();
    }

    [RelayCommand]
    public void ClearTargetSnippet()
    {
        SelectedTargetSnippet = null;
        SafeStatusMessage = "Target snippet cleared.";
        NotifyAll();
    }

    [RelayCommand(CanExecute = nameof(CanCancelProposalCreation))]
    public void CancelProposalCreation()
    {
        _proposalCreationCancellation?.Cancel();
        ProposalCreationPhase = AedaCodeProposalCreationPhase.Cancelled;
        SafeStatusMessage = "Cancelling proposal creation.";
        NotifyAll();
    }

    [RelayCommand(CanExecute = nameof(CanDryRun))]
    public async Task DryRunSelectedProposalAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProposal is null || SelectedWorkspace is null)
        {
            return;
        }

        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var targetProposalId = SelectedProposal.ProposalId;
        var request = new PatchApplyRequest(targetProposalId, targetWorkspaceId);

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId &&
            _workspaceGeneration == generation &&
            SelectedProposal?.ProposalId == targetProposalId;

        try
        {
            IsBusy = true;
            var plan = await _moduleService.DryRunApplyAsync(request, cancellationToken);
            if (!IsCurrentOperation() ||
                plan.WorkspaceId != targetWorkspaceId ||
                plan.ProposalId != targetProposalId)
            {
                return;
            }

            DryRunPlan = plan;
            if (DryRunPlan.Status != PatchApplyStatus.DryRunPassed)
            {
                ClearApplyApprovalState();
            }

            var status = IsDryRunStale
                ? "Proposal is stale. Create a fresh proposal from the current file state."
                : DryRunPlan.Status == PatchApplyStatus.DryRunPassed
                    ? "Dry run passed."
                    : "Dry run completed with safe blockers.";
            await LoadRecentCodeTasksAsync(cancellationToken, refreshSelectedTimeline: true);
            if (IsCurrentOperation() && ReferenceEquals(DryRunPlan, plan))
            {
                SafeStatusMessage = status;
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Dry run cancelled.";
            }
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Dry run failed safely.";
            }
        }
        finally
        {
            IsBusy = false;
            NotifyAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRequestApplyApproval))]
    public async Task RequestApplyApprovalAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProposal is null || SelectedWorkspace is null)
        {
            return;
        }

        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var targetProposalId = SelectedProposal.ProposalId;

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId &&
            _workspaceGeneration == generation &&
            SelectedProposal?.ProposalId == targetProposalId;

        try
        {
            var request = await _moduleService.RequestApplyApprovalAsync(
                targetProposalId,
                targetWorkspaceId,
                cancellationToken);
            if (!IsCurrentOperation() ||
                !IsApplyApprovalRequestFor(request, targetWorkspaceId, targetProposalId))
            {
                return;
            }

            ApplyApprovalRequest = request;
            ApplyApprovalDecision = null;
            await LoadRecentCodeTasksAsync(cancellationToken, refreshSelectedTimeline: true);
            if (IsCurrentOperation() && ApplyApprovalRequest?.RequestId == request.RequestId)
            {
                SafeStatusMessage = "Apply approval requested.";
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Apply approval request cancelled.";
            }
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Could not request apply approval safely.";
            }
        }
        finally
        {
            NotifyAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDecideApplyApproval))]
    public async Task AllowApplyOnceAsync(CancellationToken cancellationToken = default)
    {
        if (ApplyApprovalRequest is null || SelectedWorkspace is null || SelectedProposal is null)
        {
            return;
        }

        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var targetProposalId = SelectedProposal.ProposalId;
        var request = ApplyApprovalRequest;
        if (!IsApplyApprovalRequestFor(request, targetWorkspaceId, targetProposalId))
        {
            return;
        }

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId &&
            _workspaceGeneration == generation &&
            SelectedProposal?.ProposalId == targetProposalId &&
            ApplyApprovalRequest?.RequestId == request.RequestId;

        var decision = await _approvalStore.DecideAsync(
            request,
            ApprovalDecisionKind.AllowOnce,
            "Allowed from AEDA Code workflow.",
            cancellationToken);
        if (!IsCurrentOperation() || decision.RequestId != request.RequestId)
        {
            return;
        }

        ApplyApprovalDecision = decision;
        await LoadRecentCodeTasksAsync(cancellationToken, refreshSelectedTimeline: true);
        if (IsCurrentOperation() && ApplyApprovalDecision?.DecisionId == decision.DecisionId)
        {
            SafeStatusMessage = "Apply approved once.";
        }

        NotifyAll();
    }

    [RelayCommand(CanExecute = nameof(CanDecideApplyApproval))]
    public async Task DenyApplyApprovalAsync(CancellationToken cancellationToken = default)
    {
        if (ApplyApprovalRequest is null || SelectedWorkspace is null || SelectedProposal is null)
        {
            return;
        }

        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var targetProposalId = SelectedProposal.ProposalId;
        var request = ApplyApprovalRequest;
        if (!IsApplyApprovalRequestFor(request, targetWorkspaceId, targetProposalId))
        {
            return;
        }

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId &&
            _workspaceGeneration == generation &&
            SelectedProposal?.ProposalId == targetProposalId &&
            ApplyApprovalRequest?.RequestId == request.RequestId;

        var decision = await _approvalStore.DecideAsync(
            request,
            ApprovalDecisionKind.Deny,
            "Denied from AEDA Code workflow.",
            cancellationToken);
        if (!IsCurrentOperation() || decision.RequestId != request.RequestId)
        {
            return;
        }

        ApplyApprovalDecision = decision;
        await LoadRecentCodeTasksAsync(cancellationToken, refreshSelectedTimeline: true);
        if (IsCurrentOperation() && ApplyApprovalDecision?.DecisionId == decision.DecisionId)
        {
            SafeStatusMessage = "Apply approval denied.";
        }

        NotifyAll();
    }

    [RelayCommand(CanExecute = nameof(CanApplyApprovedProposal))]
    public async Task ApplyApprovedProposalAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProposal is null ||
            SelectedWorkspace is null ||
            ApplyApprovalRequest is null ||
            ApplyApprovalDecision is null)
        {
            return;
        }

        // Capture the target identity and everything the request needs up
        // front: SelectedWorkspace/SelectedProposal/the approval state may
        // all change (or belong to a newer selection entirely) while this
        // apply is in flight.
        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var request = new PatchApplyRequest(
            SelectedProposal.ProposalId,
            targetWorkspaceId,
            ApplyApprovalRequest,
            ApplyApprovalDecision);

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId && _workspaceGeneration == generation;

        try
        {
            IsBusy = true;
            var result = await _moduleService.ApplyApprovedProposalAsync(request, cancellationToken);

            if (IsCurrentOperation() && result.WorkspaceId == targetWorkspaceId)
            {
                ApplyResult = result;
                AddOrUpdateApplyResult(result);
                SafeStatusMessage = result.Status == PatchApplyStatus.Applied
                    ? "Proposal applied."
                    : "Apply completed with safe blockers.";
                ClearApplyApprovalState();
            }
            // else: the user switched workspaces while this apply was in
            // flight. PatchApplyService already persisted the result
            // server-side (untouched by this repair) - it will appear the
            // next time this workspace's Apply history is legitimately
            // reloaded. It must not be surfaced, and the now-current
            // workspace's own status/approval state must not be disturbed,
            // by this stale completion.

            await RefreshCodeTasksPreservingStatusAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Apply cancelled.";
                ClearApplyApprovalState();
            }
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Apply failed safely.";
                ClearApplyApprovalState();
            }
        }
        finally
        {
            IsBusy = false;
            NotifyAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateValidationRun))]
    public async Task CreateValidationRunAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedWorkspace is null || SelectedValidationTemplate is null)
        {
            return;
        }

        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var targetTemplateId = SelectedValidationTemplate.Id;
        var targetProposalId = SelectedProposal?.ProposalId;
        var targetApplyResultId = ApplyResult?.Id;
        var request = new ValidationRunRequest(
            targetWorkspaceId,
            targetTemplateId,
            ".",
            targetProposalId,
            targetApplyResultId);

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId &&
            _workspaceGeneration == generation &&
            SelectedValidationTemplate?.Id == targetTemplateId &&
            SelectedProposal?.ProposalId == targetProposalId &&
            ApplyResult?.Id == targetApplyResultId;

        try
        {
            var run = await _moduleService.CreateValidationRunAsync(request, cancellationToken);
            if (!IsCurrentOperation() ||
                run.WorkspaceId != targetWorkspaceId ||
                run.TemplateId != targetTemplateId ||
                run.ProposalId != targetProposalId ||
                run.ApplyResultId != targetApplyResultId)
            {
                return;
            }

            ValidationRun = run;
            AddOrUpdateValidationRun(run);
            ValidationApprovalRequest = null;
            ValidationApprovalDecision = null;
            ValidationOutputPreview = "Validation run created. Request approval before running it.";
            await LoadRecentCodeTasksAsync(cancellationToken, refreshSelectedTimeline: true);
            if (IsCurrentOperation() && ValidationRun?.Id == run.Id)
            {
                SafeStatusMessage = "Validation run created.";
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Validation run creation cancelled.";
            }
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Validation run could not be created safely.";
            }
        }
        finally
        {
            NotifyAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRequestValidationApproval))]
    public async Task RequestValidationApprovalAsync(CancellationToken cancellationToken = default)
    {
        if (ValidationRun is null ||
            SelectedWorkspace is null ||
            ValidationRun.WorkspaceId != SelectedWorkspace.WorkspaceId)
        {
            return;
        }

        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var targetRunId = ValidationRun.Id;

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId &&
            _workspaceGeneration == generation &&
            ValidationRun?.Id == targetRunId &&
            ValidationRun.WorkspaceId == targetWorkspaceId;

        try
        {
            var request = await _moduleService.RequestValidationApprovalAsync(
                targetRunId,
                cancellationToken);
            if (!IsCurrentOperation() || !IsValidationApprovalRequestFor(request, targetWorkspaceId, targetRunId))
            {
                return;
            }

            ValidationApprovalRequest = request;
            ValidationApprovalDecision = null;
            await LoadRecentCodeTasksAsync(cancellationToken, refreshSelectedTimeline: true);
            if (IsCurrentOperation() && ValidationApprovalRequest?.RequestId == request.RequestId)
            {
                SafeStatusMessage = "Validation approval requested.";
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Validation approval request cancelled.";
            }
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Could not request validation approval safely.";
            }
        }
        finally
        {
            NotifyAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDecideValidationApproval))]
    public async Task AllowValidationOnceAsync(CancellationToken cancellationToken = default)
    {
        if (ValidationApprovalRequest is null || ValidationRun is null || SelectedWorkspace is null)
        {
            return;
        }

        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var targetRunId = ValidationRun.Id;
        var request = ValidationApprovalRequest;
        if (ValidationRun.WorkspaceId != targetWorkspaceId ||
            !IsValidationApprovalRequestFor(request, targetWorkspaceId, targetRunId))
        {
            return;
        }

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId &&
            _workspaceGeneration == generation &&
            ValidationRun?.Id == targetRunId &&
            ValidationRun.WorkspaceId == targetWorkspaceId &&
            ValidationApprovalRequest?.RequestId == request.RequestId;

        var decision = await _approvalStore.DecideAsync(
            request,
            ApprovalDecisionKind.AllowOnce,
            "Allowed from AEDA Code workflow.",
            cancellationToken);
        if (!IsCurrentOperation() || decision.RequestId != request.RequestId)
        {
            return;
        }

        ValidationApprovalDecision = decision;
        await LoadRecentCodeTasksAsync(cancellationToken, refreshSelectedTimeline: true);
        if (IsCurrentOperation() && ValidationApprovalDecision?.DecisionId == decision.DecisionId)
        {
            SafeStatusMessage = "Validation approved once.";
        }

        NotifyAll();
    }

    [RelayCommand(CanExecute = nameof(CanDecideValidationApproval))]
    public async Task DenyValidationApprovalAsync(CancellationToken cancellationToken = default)
    {
        if (ValidationApprovalRequest is null || ValidationRun is null || SelectedWorkspace is null)
        {
            return;
        }

        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var targetRunId = ValidationRun.Id;
        var request = ValidationApprovalRequest;
        if (ValidationRun.WorkspaceId != targetWorkspaceId ||
            !IsValidationApprovalRequestFor(request, targetWorkspaceId, targetRunId))
        {
            return;
        }

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId &&
            _workspaceGeneration == generation &&
            ValidationRun?.Id == targetRunId &&
            ValidationRun.WorkspaceId == targetWorkspaceId &&
            ValidationApprovalRequest?.RequestId == request.RequestId;

        var decision = await _approvalStore.DecideAsync(
            request,
            ApprovalDecisionKind.Deny,
            "Denied from AEDA Code workflow.",
            cancellationToken);
        if (!IsCurrentOperation() || decision.RequestId != request.RequestId)
        {
            return;
        }

        ValidationApprovalDecision = decision;
        await LoadRecentCodeTasksAsync(cancellationToken, refreshSelectedTimeline: true);
        if (IsCurrentOperation() && ValidationApprovalDecision?.DecisionId == decision.DecisionId)
        {
            SafeStatusMessage = "Validation approval denied.";
        }

        NotifyAll();
    }

    [RelayCommand(CanExecute = nameof(CanRunApprovedValidation))]
    public async Task RunApprovedValidationAsync(CancellationToken cancellationToken = default)
    {
        if (ValidationRun is null ||
            SelectedWorkspace is null ||
            ValidationApprovalRequest is null ||
            ValidationApprovalDecision is null ||
            ValidationRun.WorkspaceId != SelectedWorkspace.WorkspaceId ||
            !ValidationApprovalDecision.IsAllowed ||
            ValidationApprovalDecision.RequestId != ValidationApprovalRequest.RequestId ||
            !IsValidationApprovalRequestFor(
                ValidationApprovalRequest,
                SelectedWorkspace.WorkspaceId,
                ValidationRun.Id))
        {
            return;
        }

        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var targetRunId = ValidationRun.Id;
        var request = ValidationApprovalRequest;
        var decision = ValidationApprovalDecision;

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId &&
            _workspaceGeneration == generation &&
            ValidationRun?.Id == targetRunId &&
            ValidationRun.WorkspaceId == targetWorkspaceId &&
            ValidationApprovalRequest?.RequestId == request.RequestId &&
            ValidationApprovalDecision?.DecisionId == decision.DecisionId;

        try
        {
            IsBusy = true;
            var run = await _moduleService.RunApprovedValidationAsync(
                targetRunId,
                request,
                decision,
                cancellationToken);
            if (!IsCurrentOperation() || run.Id != targetRunId || run.WorkspaceId != targetWorkspaceId)
            {
                return;
            }

            ValidationRun = run;
            AddOrUpdateValidationRun(run);
            ValidationOutputPreview = BuildValidationOutput(run);
            await LoadRecentCodeTasksAsync(cancellationToken, refreshSelectedTimeline: true);
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Validation run completed.";
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Validation run cancelled.";
            }
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Validation failed safely.";
            }
        }
        finally
        {
            IsBusy = false;
            if (IsCurrentOperation())
            {
                ValidationApprovalRequest = null;
                ValidationApprovalDecision = null;
            }

            NotifyAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRollback))]
    public async Task RollbackSelectedApplyResultAsync(CancellationToken cancellationToken = default)
    {
        if (ApplyResult is null || SelectedWorkspace is null)
        {
            return;
        }

        // Capture the target identity before awaiting: the user may switch
        // workspaces (or select a different apply result) while this
        // rollback is in flight. The backend rollback is intentionally NOT
        // cancelled on a workspace switch - it has already run destructive,
        // persisted file operations - so it must be allowed to complete;
        // only its presentation must be withheld once stale.
        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var targetApplyResultId = ApplyResult.Id;
        var request = new PatchRollbackRequest(targetApplyResultId, targetWorkspaceId);

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId &&
            _workspaceGeneration == generation &&
            ApplyResult?.Id == targetApplyResultId;

        try
        {
            IsBusy = true;
            var result = await _moduleService.RollbackAsync(request, cancellationToken);

            if (!IsCurrentOperation() || result.ApplyResultId != targetApplyResultId)
            {
                // The user switched workspaces (or selected a different
                // apply result) while this rollback was in flight. The
                // backend rollback already persisted - it will appear
                // correctly the next time this workspace's Apply history is
                // legitimately reloaded. It must not be surfaced, and the
                // now-current workspace's own state must not be disturbed,
                // by this stale completion.
                return;
            }

            RollbackResult = result;
            SafeStatusMessage = RollbackResult.Status == PatchApplyStatus.RolledBack
                ? "Rollback completed."
                : "Rollback completed with safe blockers.";
            await RefreshCodeTasksPreservingStatusAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Rollback cancelled.";
            }
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Rollback failed safely.";
            }
        }
        finally
        {
            IsBusy = false;
            NotifyAll();
        }
    }

    [RelayCommand]
    public async Task SelectApplyResultAsync(
        AedaCodeApplyItem? item,
        CancellationToken cancellationToken = default)
    {
        if (item is null)
        {
            ApplyResult = null;
            RollbackResult = null;
            _selectedApplyResultAlreadyRolledBack = false;
            NotifyAll();
            return;
        }

        if (SelectedWorkspace is null)
        {
            SafeStatusMessage = "Select a workspace before choosing an apply result.";
            NotifyAll();
            return;
        }

        // Capture the target identity before either await below: the user
        // may switch workspaces (or select a different apply result) while
        // either service call is in flight.
        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var targetApplyResultId = item.ApplyResultId;

        bool IsCurrentOperation() =>
            SelectedWorkspace?.WorkspaceId == targetWorkspaceId && _workspaceGeneration == generation;

        try
        {
            IsBusy = true;
            var result = await _moduleService.GetApplyResultAsync(targetApplyResultId, cancellationToken);
            if (!IsCurrentOperation())
            {
                // The user has since moved to a different workspace (or
                // generation) - this apply result, whether found or not,
                // must not replace or clear whatever is now selected.
                return;
            }

            if (result is null || result.WorkspaceId != targetWorkspaceId)
            {
                ApplyResult = null;
                RollbackResult = null;
                _selectedApplyResultAlreadyRolledBack = false;
                SafeStatusMessage = "Selected apply result is no longer available for this workspace.";
                return;
            }

            var proposal = await _moduleService.GetProposalAsync(result.ProposalId, cancellationToken);
            if (!IsCurrentOperation())
            {
                return;
            }

            ApplyResult = result;
            RollbackResult = null;
            _selectedApplyResultAlreadyRolledBack = proposal?.Status == PatchProposalStatus.RolledBack;
            SafeStatusMessage = "Apply result loaded from history.";
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentOperation())
            {
                SafeStatusMessage = "Apply result load cancelled.";
            }
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            if (IsCurrentOperation())
            {
                ApplyResult = null;
                RollbackResult = null;
                _selectedApplyResultAlreadyRolledBack = false;
                SafeStatusMessage = "Apply result is temporarily unavailable.";
            }
        }
        finally
        {
            IsBusy = false;
            NotifyAll();
        }
    }

    [RelayCommand]
    public async Task SelectTaskAsync(
        AedaTaskSummary? task,
        CancellationToken cancellationToken = default)
    {
        SelectedTask = task;
        SelectedTaskTimeline.Clear();
        CodeTimelineGroups.Clear();
        if (task is null)
        {
            SafeStatusMessage = "Select a Code task to view its timeline.";
            NotifyAll();
            return;
        }

        var groups = await _taskCenterService.GetTimelineAsync(
            task.Id,
            SummaryLimit,
            cancellationToken);
        foreach (var group in groups)
        {
            SelectedTaskTimeline.Add(group);
            CodeTimelineGroups.Add(AedaCodeTimelineGroupItem.From(group));
        }

        SafeStatusMessage = groups.Count == 0
            ? "No safe timeline events are available for this task."
            : "Code task timeline loaded.";
        NotifyAll();
    }

    [RelayCommand]
    public void OpenRelatedTaskInTaskCenter()
    {
        SafeStatusMessage = SelectedTask is null
            ? "Select a task, then open Task Center for the full timeline."
            : "Open Task Center to inspect the selected task.";
    }

    [RelayCommand]
    public void BackToDashboard()
    {
        SafeStatusMessage = "Use Dashboard to return to the module overview.";
    }

    private bool CanStartSession() => !IsBusy && SelectedWorkspace is not null;

    private bool CanCreateProposal() =>
        !IsBusy &&
        !IsCreatingProposal &&
        SelectedWorkspace is not null &&
        !string.IsNullOrWhiteSpace(ProposalRequest) &&
        ProposalRequest.Length <= MaxProposalRequestCharacters &&
        ProposalTitle.Length <= MaxProposalTitleCharacters &&
        !HasSelectedContextWarning;

    private bool CanSearchContextFiles() =>
        !IsBusy &&
        !IsSearchingContext &&
        SelectedWorkspace is not null;

    private bool CanAddContextFile(AedaCodeContextFileCandidate? candidate) =>
        !IsBusy &&
        SelectedWorkspace is not null &&
        candidate is { IsReadable: true, IsAlreadySelected: false } &&
        SelectedContextFiles.Count < MaxSelectedContextFiles;

    private bool CanCancelProposalCreation() => IsCreatingProposal;

    private bool CanDryRun() => !IsBusy && SelectedProposal is not null && SelectedWorkspace is not null;

    private bool CanRequestApplyApproval() =>
        !IsBusy &&
        SelectedProposal is not null &&
        SelectedWorkspace is not null &&
        DryRunPlan?.Status == PatchApplyStatus.DryRunPassed;

    private bool CanDecideApplyApproval() =>
        !IsBusy &&
        ApplyApprovalRequest is not null &&
        ApplyApprovalDecision is null;

    private bool CanApplyApprovedProposal() =>
        !IsBusy &&
        SelectedProposal is not null &&
        SelectedWorkspace is not null &&
        DryRunPlan?.Status == PatchApplyStatus.DryRunPassed &&
        DryRunPlan.ProposalId == SelectedProposal.ProposalId &&
        DryRunPlan.WorkspaceId == SelectedWorkspace.WorkspaceId &&
        ApplyApprovalRequest is not null &&
        ApplyApprovalDecision?.IsAllowed == true &&
        IsApplyApprovalForSelection();

    private bool CanCreateValidationRun() =>
        !IsBusy &&
        SelectedWorkspace is not null &&
        SelectedValidationTemplate is not null &&
        (SelectedProposal is not null || ApplyResult is not null);

    private bool CanRequestValidationApproval() =>
        !IsBusy &&
        SelectedWorkspace is not null &&
        ValidationRun is { Status: ValidationRunStatus.Created } &&
        ValidationRun.WorkspaceId == SelectedWorkspace.WorkspaceId;

    private bool CanDecideValidationApproval() =>
        !IsBusy &&
        SelectedWorkspace is not null &&
        ValidationRun is not null &&
        ValidationRun.WorkspaceId == SelectedWorkspace.WorkspaceId &&
        ValidationApprovalRequest is not null &&
        ValidationApprovalDecision is null &&
        IsValidationApprovalRequestFor(
            ValidationApprovalRequest,
            SelectedWorkspace.WorkspaceId,
            ValidationRun.Id);

    private bool CanRunApprovedValidation() =>
        !IsBusy &&
        SelectedWorkspace is not null &&
        ValidationRun is not null &&
        ValidationRun.WorkspaceId == SelectedWorkspace.WorkspaceId &&
        ValidationApprovalRequest is not null &&
        ValidationApprovalDecision?.IsAllowed == true &&
        ValidationApprovalDecision.RequestId == ValidationApprovalRequest.RequestId &&
        IsValidationApprovalRequestFor(
            ValidationApprovalRequest,
            SelectedWorkspace.WorkspaceId,
            ValidationRun.Id);

    private bool CanRollback() => !IsBusy && CanShowRollback && SelectedWorkspace is not null;

    private bool IsApplyApprovalForSelection()
    {
        if (SelectedProposal is null ||
            SelectedWorkspace is null ||
            ApplyApprovalRequest is null)
        {
            return false;
        }

        return IsApplyApprovalRequestFor(
            ApplyApprovalRequest,
            SelectedWorkspace.WorkspaceId,
            SelectedProposal.ProposalId);
    }

    private static bool IsApplyApprovalRequestFor(
        ApprovalRequest request,
        WorkspaceId workspaceId,
        PatchProposalId proposalId)
    {
        var expected = $"patch-apply:{workspaceId}:{proposalId}";
        return request.Scope.Kind == ApprovalKind.ApproveFutureApply &&
            request.Scope.NormalizedResourceScope.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidationApprovalRequestFor(
        ApprovalRequest request,
        WorkspaceId workspaceId,
        ValidationRunId runId)
    {
        var expected = $"validation-run:{workspaceId}:{runId}";
        return request.Scope.Kind == ApprovalKind.ValidationRun &&
            request.Scope.NormalizedResourceScope.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommandStates();

    partial void OnSelectedWorkspaceChanging(AedaCodeWorkspaceItem? oldValue, AedaCodeWorkspaceItem? newValue)
    {
        if (oldValue?.WorkspaceId == newValue?.WorkspaceId)
        {
            return;
        }

        // The workspace identity is genuinely transitioning - whether this
        // assignment came from Avalonia's two-way binding (which can run
        // before the SelectWorkspaceAsync command executes) or from the
        // command itself. Clear every workspace-scoped presentation
        // snapshot right here, synchronously, before SelectedWorkspace
        // actually changes, so the previous workspace's proposals,
        // validation templates, selection/detail/context/approval/rollback
        // state, and Apply-history can never remain visible under the new
        // one - regardless of who triggered the assignment, in what order,
        // or how slowly (or unsuccessfully) the new workspace's own load
        // completes.
        Proposals.Clear();
        ValidationTemplates.Clear();
        SelectedValidationTemplate = null;
        ContextFileCandidates.Clear();
        SelectedContextFiles.Clear();
        TargetSnippetCandidates.Clear();
        SelectedTargetSnippet = null;
        ClearProposalDetail();
        ApplyResults.Clear();
        ValidationRuns.Clear();
        _applyHistoryWorkspaceId = null;
        _workspaceTransitionPending = true;
        _workspaceGeneration++;
    }

    partial void OnSelectedWorkspaceChanged(AedaCodeWorkspaceItem? value)
    {
        OnPropertyChanged(nameof(HasRollbackAvailable));
        OnPropertyChanged(nameof(CanShowRollback));
        OnPropertyChanged(nameof(RollbackAvailabilityText));
        OnPropertyChanged(nameof(ApplyResultDetailText));
        NotifyCommandStates();
    }

    partial void OnSelectedProposalChanged(AedaCodeProposalItem? value) => NotifyCommandStates();

    partial void OnSelectedValidationTemplateChanged(AedaCodeValidationTemplateItem? value) => NotifyCommandStates();

    partial void OnProposalRequestChanged(string value) => NotifyCommandStates();

    partial void OnProposalTitleChanged(string value) => NotifyCommandStates();

    partial void OnContextFileSearchQueryChanged(string value) => NotifyCommandStates();

    partial void OnIsSearchingContextChanged(bool value) => NotifyCommandStates();

    partial void OnIsCreatingProposalChanged(bool value) => NotifyCommandStates();

    partial void OnDashboardChanged(AedaCodeDashboardModel? value)
    {
        if (value is not null)
        {
            ApplyDashboard(value);
        }

        NotifyAll();
    }

    private void LoadRegisteredWorkspaces()
    {
        var selectedId = SelectedWorkspace?.WorkspaceId;
        Workspaces.Clear();
        foreach (var workspace in _workspaceRegistry.List())
        {
            Workspaces.Add(AedaCodeWorkspaceItem.From(workspace));
        }

        SelectedWorkspace = Workspaces.FirstOrDefault(workspace => workspace.WorkspaceId == selectedId)
            ?? Workspaces.FirstOrDefault();
    }

    private void RefreshContextCandidateSelectionState()
    {
        var selected = new HashSet<string>(
            SelectedContextFiles.Select(file => file.RelativePath),
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < ContextFileCandidates.Count; index++)
        {
            var candidate = ContextFileCandidates[index];
            ContextFileCandidates[index] = candidate with
            {
                IsAlreadySelected = selected.Contains(candidate.RelativePath)
            };
        }
    }

    private async Task RefreshTargetSnippetCandidatesAsync(CancellationToken cancellationToken)
    {
        TargetSnippetCandidates.Clear();
        SelectedTargetSnippet = null;
        if (SelectedWorkspace is null || SelectedContextFiles.Count == 0)
        {
            return;
        }

        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;
        var selectedRelativePaths = SelectedContextFiles.Select(file => file.RelativePath).ToArray();

        var candidates = await _moduleService.ListTargetSnippetCandidatesAsync(
            new AedaCodeTargetSnippetRequest(
                targetWorkspaceId,
                selectedRelativePaths),
            cancellationToken);
        if (SelectedWorkspace?.WorkspaceId != targetWorkspaceId ||
            _workspaceGeneration != generation ||
            !selectedRelativePaths.SequenceEqual(
                SelectedContextFiles.Select(file => file.RelativePath),
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            TargetSnippetCandidates.Add(candidate);
        }
    }

    private void RemoveTargetSnippetsForFile(string relativePath)
    {
        for (var index = TargetSnippetCandidates.Count - 1; index >= 0; index--)
        {
            if (string.Equals(
                    TargetSnippetCandidates[index].RelativePath,
                    relativePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                TargetSnippetCandidates.RemoveAt(index);
            }
        }

        if (SelectedTargetSnippet is not null &&
            string.Equals(
                SelectedTargetSnippet.RelativePath,
                relativePath,
                StringComparison.OrdinalIgnoreCase))
        {
            SelectedTargetSnippet = null;
        }
    }

    private async Task LoadWorkspaceWorkflowAsync(CancellationToken cancellationToken)
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        // Capture the target identity once, up front. Both service calls
        // below are awaited, and SelectedWorkspace/_workspaceGeneration may
        // change out from under us between them (or before either
        // completes) if the user switches workspaces while this load is in
        // flight - re-reading the mutable property after an await would let
        // a stale request commit under a different workspace's bindings.
        var targetWorkspaceId = SelectedWorkspace.WorkspaceId;
        var generation = _workspaceGeneration;

        var proposals = await _moduleService.ListProposalSummariesAsync(
            targetWorkspaceId,
            SummaryLimit,
            cancellationToken);

        var templates = await _moduleService.ListValidationTemplatesAsync(
            targetWorkspaceId,
            cancellationToken);

        if (SelectedWorkspace?.WorkspaceId != targetWorkspaceId || _workspaceGeneration != generation)
        {
            // The user has moved to a different workspace (or transitioned
            // away and back) since this load started; discard this stale
            // result silently rather than committing mixed or out-of-date
            // proposals/templates under whatever workspace is now selected.
            return;
        }

        // Commit the complete workspace snapshot together, only now that
        // both reads have succeeded and the workspace/generation is still
        // current - never a partially-cleared or mixed-workspace state.
        Proposals.Clear();
        foreach (var proposal in proposals
                     .OrderByDescending(proposal => proposal.UpdatedAtUtc)
                     .ThenBy(proposal => proposal.ProposalId.ToString(), StringComparer.Ordinal))
        {
            Proposals.Add(AedaCodeProposalItem.From(proposal));
        }

        ValidationTemplates.Clear();
        foreach (var template in templates)
        {
            ValidationTemplates.Add(AedaCodeValidationTemplateItem.From(template));
        }

        SelectedValidationTemplate = ValidationTemplates.FirstOrDefault();
    }

    private async Task LoadRecentCodeTasksAsync(
        CancellationToken cancellationToken,
        bool refreshSelectedTimeline = false)
    {
        var selectedTaskId = SelectedTask?.Id;
        RecentCodeTasks.Clear();
        var tasks = await _taskCenterService.ListTasksByModuleAsync(
            AedaTaskCenterModule.Code,
            SummaryLimit,
            cancellationToken);
        foreach (var task in tasks)
        {
            RecentCodeTasks.Add(task);
        }

        SelectedTask = selectedTaskId is null
            ? SelectedTask ?? RecentCodeTasks.FirstOrDefault()
            : RecentCodeTasks.FirstOrDefault(task => task.Id == selectedTaskId) ??
                RecentCodeTasks.FirstOrDefault();
        if (SelectedTask is not null &&
            (refreshSelectedTimeline || SelectedTaskTimeline.Count == 0))
        {
            await SelectTaskAsync(SelectedTask, cancellationToken);
        }
    }

    private async Task RefreshCodeTasksPreservingStatusAsync(CancellationToken cancellationToken)
    {
        var status = SafeStatusMessage;
        await LoadRecentCodeTasksAsync(cancellationToken, refreshSelectedTimeline: true);
        SafeStatusMessage = status;
    }

    /// <summary>
    /// Applies a dashboard's data to the dashboard-owned presentation
    /// collections. This method never touches <see cref="SelectedWorkspace"/>
    /// - that is user/navigation state, and a session dashboard is data that
    /// must never override a newer user selection. Every caller MUST verify,
    /// immediately before invoking this (i.e. before assigning
    /// <see cref="Dashboard"/>, whose setter triggers this), that
    /// <c>dashboard.Workspace.WorkspaceId</c> still matches the currently
    /// selected workspace and the current <see cref="_workspaceGeneration"/>.
    /// </summary>
    private void ApplyDashboard(AedaCodeDashboardModel dashboard)
    {
        Proposals.Clear();
        foreach (var proposal in dashboard.Proposals
                     .OrderByDescending(proposal => proposal.UpdatedAtUtc)
                     .ThenBy(proposal => proposal.ProposalId.ToString(), StringComparer.Ordinal)
                     .Take(SummaryLimit))
        {
            Proposals.Add(AedaCodeProposalItem.From(proposal));
        }

        ApplyResults.Clear();
        foreach (var result in dashboard.ApplyResults
                     .OrderByDescending(result => result.UpdatedAtUtc)
                     .Take(SummaryLimit))
        {
            ApplyResults.Add(AedaCodeApplyItem.From(result));
        }

        ValidationRuns.Clear();
        foreach (var run in dashboard.ValidationRuns
                     .OrderByDescending(run => run.UpdatedAtUtc)
                     .Take(SummaryLimit))
        {
            ValidationRuns.Add(AedaCodeValidationRunItem.From(run));
        }

        // The caller has already validated that this dashboard belongs to
        // the currently selected workspace and generation, so its
        // ApplyResults are now authoritative for that workspace.
        _applyHistoryWorkspaceId = dashboard.Workspace.WorkspaceId;
    }

    private void AddOrUpdateApplyResult(PatchApplyResult result)
    {
        var existing = ApplyResults.FirstOrDefault(item => item.ApplyResultId == result.Id);
        if (existing is not null)
        {
            ApplyResults.Remove(existing);
        }

        ApplyResults.Insert(0, AedaCodeApplyItem.From(result));
        while (ApplyResults.Count > SummaryLimit)
        {
            ApplyResults.RemoveAt(ApplyResults.Count - 1);
        }
    }

    private void AddOrUpdateProposal(AedaCodeProposalSummary summary)
    {
        var existing = Proposals.FirstOrDefault(item => item.ProposalId == summary.ProposalId);
        if (existing is not null)
        {
            Proposals.Remove(existing);
        }

        Proposals.Insert(0, AedaCodeProposalItem.From(summary));
        while (Proposals.Count > SummaryLimit)
        {
            Proposals.RemoveAt(Proposals.Count - 1);
        }
    }

    private void AddOrUpdateValidationRun(ValidationRun run)
    {
        var existing = ValidationRuns.FirstOrDefault(item => item.RunId == run.Id);
        if (existing is not null)
        {
            ValidationRuns.Remove(existing);
        }

        ValidationRuns.Insert(0, AedaCodeValidationRunItem.From(run));
        while (ValidationRuns.Count > SummaryLimit)
        {
            ValidationRuns.RemoveAt(ValidationRuns.Count - 1);
        }
    }

    private void ClearProposalDetail()
    {
        SelectedProposal = null;
        SelectedProposalDetail = null;
        ProposalFiles.Clear();
        UnifiedDiffPreview = "Select a proposal to preview its unified diff.";
        ValidationPlanText = "Select a proposal to view validation guidance.";
        HashStatusText = "No proposal selected.";
        SourceSummaryText = "No source context loaded.";
        ClearActionState();
    }

    private void ClearActionState()
    {
        DryRunPlan = null;
        ClearApplyApprovalState();
        ApplyResult = null;
        ValidationRun = null;
        ValidationApprovalRequest = null;
        ValidationApprovalDecision = null;
        RollbackResult = null;
        _selectedApplyResultAlreadyRolledBack = false;
        ValidationOutputPreview = "Run an approved validation to view sanitized output.";
    }

    private void ClearApplyApprovalState()
    {
        ApplyApprovalRequest = null;
        ApplyApprovalDecision = null;
    }

    private static string FormatReasons<TReason>(IReadOnlyList<TReason> reasons)
        where TReason : struct, Enum
    {
        if (reasons.Count == 0)
        {
            return "none reported";
        }

        return string.Join(", ", reasons.Take(4).Select(reason => reason.ToString()));
    }

    private static string BuildBoundedDiff(PatchProposal proposal)
    {
        var lines = proposal.Files
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .SelectMany(file => NormalizeDiffLines(file))
            .Take(DiffLineLimit)
            .ToArray();
        var text = string.Join(Environment.NewLine, lines);
        if (text.Length > DiffCharacterLimit)
        {
            text = text[..DiffCharacterLimit] + Environment.NewLine + "[diff truncated]";
        }

        return string.IsNullOrWhiteSpace(text)
            ? "This proposal has no unified diff preview."
            : text;
    }

    private static IEnumerable<string> NormalizeDiffLines(PatchProposalFile file)
    {
        yield return $"diff -- {SafeRelativePath(file.RelativePath)}";
        yield return $"change: {file.ChangeKind}";
        foreach (var rawLine in file.UnifiedDiff.Split(
                     ["\r\n", "\n"],
                     StringSplitOptions.None))
        {
            yield return RedactSensitiveText(RemoveAbsolutePaths(rawLine));
        }
    }

    private static string BuildValidationPlanText(PatchProposalValidationPlan plan)
    {
        var commands = plan.SuggestedCommands
            .Take(4)
            .Select(command => $"{RedactSensitiveText(command.Command)} · {RedactSensitiveText(command.Rationale)}");
        var checks = plan.ManualChecks
            .Take(4)
            .Select(check => $"Manual: {RedactSensitiveText(check)}");
        var text = string.Join(Environment.NewLine, commands.Concat(checks));
        return string.IsNullOrWhiteSpace(text)
            ? "No validation guidance was attached to this proposal."
            : text;
    }

    private static string BuildHashStatusText(PatchProposal proposal)
    {
        var mismatches = proposal.Files.Count(file =>
            string.IsNullOrWhiteSpace(file.OriginalContentHash) ||
            string.IsNullOrWhiteSpace(file.ProposedContentHash));
        return mismatches == 0
            ? "Original/proposed hashes are present for all proposal files."
            : $"{mismatches} file(s) have incomplete hash metadata.";
    }

    private static string BuildSourceSummaryText(PatchProposal proposal)
    {
        if (proposal.Sources.Count == 0)
        {
            return "No source context was attached.";
        }

        var files = proposal.Sources
            .Select(source => SafeRelativePath(source.RelativePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var preview = string.Join(", ", files.Take(6));
        return files.Length <= 6
            ? $"Context: {files.Length} file(s) - {preview}"
            : $"Context: {files.Length} file(s) - {preview}, ...";
    }

    private static string BuildValidationOutput(ValidationRun run)
    {
        if (run.CommandResult is null)
        {
            return $"{run.Status}: no command output was captured.";
        }

        var stdout = BoundOutput(run.CommandResult.Stdout.Text);
        var stderr = BoundOutput(run.CommandResult.Stderr.Text);
        return string.Join(
            Environment.NewLine,
            [
                $"Status: {run.CommandResult.Status}",
                $"Exit code: {run.CommandResult.ExitCode?.ToString() ?? "none"}",
                $"Stdout: {stdout}",
                $"Stderr: {stderr}"
            ]);
    }

    private static string BuildValidationRunDetail(ValidationRun run)
    {
        var result = run.CommandResult;
        if (result is null)
        {
            var reasons = FormatReasons(run.FailureReasons);
            return $"Status: {run.Status}. Template: {run.TemplateId}. Reasons: {reasons}.";
        }

        var failure = result.FailureReason?.ToString()
            ?? FormatReasons(run.FailureReasons);
        return $"Status: {run.Status}. Exit code: {result.ExitCode?.ToString() ?? "none"}. Duration: {result.Duration.TotalSeconds:0.#}s. Reason: {failure}.";
    }

    private static string BoundOutput(string value)
    {
        var safe = RedactSensitiveText(RemoveAbsolutePaths(value));
        return safe.Length <= ValidationOutputLimit
            ? safe
            : safe[..ValidationOutputLimit] + " [truncated]";
    }

    private static string SafeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ".";
        }

        var trimmed = value.Trim().Replace('\\', '/');
        return Path.IsPathRooted(trimmed)
            ? Path.GetFileName(trimmed)
            : trimmed.TrimStart('/');
    }

    private static string GetContainingFolder(string relativePath)
    {
        var folder = Path.GetDirectoryName(relativePath.Replace('/', Path.DirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(folder)
            ? "."
            : folder.Replace('\\', '/');
    }

    private static string FormatSize(long? sizeBytes)
    {
        if (sizeBytes is null)
        {
            return "unknown size";
        }

        var size = sizeBytes.Value;
        return size < 1024
            ? $"{size} B"
            : size < 1024 * 1024
                ? $"{size / 1024d:0.#} KB"
                : $"{size / 1024d / 1024d:0.#} MB";
    }

    private static string RemoveAbsolutePaths(string value)
    {
        var withoutWindowsPaths = Regex.Replace(
            value,
            @"[A-Za-z]:[\\/][^\s]+",
            match => Path.GetFileName(match.Value.TrimEnd(',', ';', ':')));
        return Regex.Replace(
            withoutWindowsPaths,
            @"\\\\[^\s]+",
            "[network-path]");
    }

    private static string RedactSensitiveText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var safe = Regex.Replace(
            value,
            @"(?i)(secret|password|token|api[_-]?key)\s*[:=]\s*[^\s,;]+",
            "$1=[redacted]");
        return safe.Contains("BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase)
            ? "[redacted sensitive content]"
            : safe;
    }

    private static bool IsSafeFailure(Exception exception) =>
        exception is InvalidOperationException ||
        exception is IOException ||
        exception is ArgumentException ||
        exception is WorkspaceAccessException;

    private static AedaCodeProposalCreationFailure MapProposalCreationFailure(Exception exception)
    {
        var code = exception.Message;
        return code switch
        {
            "code_request_empty" => AedaCodeProposalCreationFailure.FromReason(AedaCodeProposalCreationFailureReason.RequestEmpty),
            "code_request_too_long" or "code_title_too_long" => AedaCodeProposalCreationFailure.FromReason(AedaCodeProposalCreationFailureReason.RequestTooLong),
            "code_context_empty" => AedaCodeProposalCreationFailure.FromReason(AedaCodeProposalCreationFailureReason.NoSafeContext),
            "model_patch_invalid" => AedaCodeProposalCreationFailure.FromReason(AedaCodeProposalCreationFailureReason.InvalidModelJson),
            "model_patch_path_not_in_context" or "model_patch_path_invalid" => AedaCodeProposalCreationFailure.FromReason(AedaCodeProposalCreationFailureReason.UnsafeFileTarget),
            "partial_proposed_content" => AedaCodeProposalCreationFailure.FromReason(AedaCodeProposalCreationFailureReason.PartialProposedContent),
            "unsafe_large_deletion" => AedaCodeProposalCreationFailure.FromReason(AedaCodeProposalCreationFailureReason.UnsafeLargeDeletion),
            "invalid_file_shape" => AedaCodeProposalCreationFailure.FromReason(AedaCodeProposalCreationFailureReason.InvalidFileShape),
            "target_text_not_found" => AedaCodeProposalCreationFailure.FromReason(AedaCodeProposalCreationFailureReason.TargetTextNotFound),
            "ambiguous_text_replacement" => AedaCodeProposalCreationFailure.FromReason(AedaCodeProposalCreationFailureReason.AmbiguousTextReplacement),
            "model_patch_content_invalid" or "model_patch_file_count_invalid" => AedaCodeProposalCreationFailure.FromReason(AedaCodeProposalCreationFailureReason.UnsafePatch),
            "remote_workspace_context_denied" or "remote_provider_disabled" => AedaCodeProposalCreationFailure.FromReason(AedaCodeProposalCreationFailureReason.ProviderRejectedByPolicy),
            "provider_capability_unavailable" or "provider_chat_unavailable" => AedaCodeProposalCreationFailure.FromReason(AedaCodeProposalCreationFailureReason.ProviderUnavailable),
            _ => AedaCodeProposalCreationFailure.FromReason(AedaCodeProposalCreationFailureReason.UnknownSafeFailure)
        };
    }

    private void OnProposalCreationProgress(AedaCodeProposalCreationProgress progress)
    {
        ProposalCreationPhase = progress.Phase;
        if (progress.SafeContextFileCount is not null)
        {
            ProposalCreationContextFileCount = progress.SafeContextFileCount;
        }

        ProposalCreationRetryAttempted |= progress.RetryAttempted;
        if (!string.IsNullOrWhiteSpace(progress.SchemaIssueCode))
        {
            ProposalCreationSchemaIssueCode = progress.SchemaIssueCode;
        }

        SafeStatusMessage = progress.Phase switch
        {
            AedaCodeProposalCreationPhase.LoadingBoundedContext => "Loading bounded workspace context. No files changed.",
            AedaCodeProposalCreationPhase.CallingCodingModel => string.IsNullOrWhiteSpace(progress.SafeProviderLabel)
                ? "Calling coding model. No files changed."
                : $"Calling {progress.SafeProviderLabel} coding model. No files changed.",
            AedaCodeProposalCreationPhase.ParsingModelDraft => "Parsing structured proposal draft. No files changed.",
            AedaCodeProposalCreationPhase.RetryingStructuredDraft => "Retrying structured draft format. No files changed.",
            AedaCodeProposalCreationPhase.ValidatingProposal => "Validating proposal safety. No files changed.",
            AedaCodeProposalCreationPhase.SavingProposal => "Saving proposal for review. No files changed.",
            AedaCodeProposalCreationPhase.Succeeded => "Proposal created for review. No files changed.",
            AedaCodeProposalCreationPhase.Cancelled => "Proposal creation cancelled. No files changed.",
            AedaCodeProposalCreationPhase.Failed => "Proposal creation failed safely. No files changed.",
            _ => "Preparing proposal request. No files changed."
        };
        NotifyAll();
    }

    private static string BuildProposalCreationFailureDetail(AedaCodeProposalCreationFailure failure)
    {
        var parts = new List<string> { $"Details: {failure.SafeCode}" };
        if (!string.IsNullOrWhiteSpace(failure.SchemaIssueCode))
        {
            parts.Add($"schema issue: {failure.SchemaIssueCode}");
        }

        if (failure.RetryAttempted)
        {
            parts.Add("structured retry: yes");
        }

        return string.Join(" | ", parts);
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(WorkspaceSummary));
        OnPropertyChanged(nameof(ProposalCreationWorkspaceText));
        OnPropertyChanged(nameof(ProposalCreationSafetyText));
        OnPropertyChanged(nameof(ProposalCreationStateText));
        OnPropertyChanged(nameof(ProposalCreationPhaseLabel));
        OnPropertyChanged(nameof(ProposalCreationProgressText));
        OnPropertyChanged(nameof(ProposalRequestLengthText));
        OnPropertyChanged(nameof(ProposalTitleLengthText));
        OnPropertyChanged(nameof(ContextSearchStatusText));
        OnPropertyChanged(nameof(SelectedContextSummaryText));
        OnPropertyChanged(nameof(SelectedContextWarningText));
        OnPropertyChanged(nameof(HasContextFileCandidates));
        OnPropertyChanged(nameof(HasSelectedContextFiles));
        OnPropertyChanged(nameof(HasTargetSnippetCandidates));
        OnPropertyChanged(nameof(HasSelectedTargetSnippet));
        OnPropertyChanged(nameof(TargetSnippetStatusText));
        OnPropertyChanged(nameof(SelectedTargetSnippetText));
        OnPropertyChanged(nameof(HasSelectedContextWarning));
        OnPropertyChanged(nameof(HasProposalCreationFailure));
        OnPropertyChanged(nameof(ProposalCreationFailureText));
        OnPropertyChanged(nameof(ProposalCreationFailureDetailText));
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(WorkingIndicatorText));
        OnPropertyChanged(nameof(SessionStatusText));
        OnPropertyChanged(nameof(RecentCodeTaskCountText));
        OnPropertyChanged(nameof(ProposalCountText));
        OnPropertyChanged(nameof(AffectedFileCountText));
        OnPropertyChanged(nameof(RiskSummary));
        OnPropertyChanged(nameof(SelectedProposalMetadataText));
        OnPropertyChanged(nameof(SelectedProposalSummaryText));
        OnPropertyChanged(nameof(SelectedProposalTimestampText));
        OnPropertyChanged(nameof(ValidationPlanText));
        OnPropertyChanged(nameof(HashStatusText));
        OnPropertyChanged(nameof(SourceSummaryText));
        OnPropertyChanged(nameof(DryRunStatusText));
        OnPropertyChanged(nameof(ReviewGateOrderText));
        OnPropertyChanged(nameof(DryRunDetailText));
        OnPropertyChanged(nameof(IsDryRunStale));
        OnPropertyChanged(nameof(StaleProposalRecoveryText));
        OnPropertyChanged(nameof(ApplyApprovalStatusText));
        OnPropertyChanged(nameof(ApplyResultText));
        OnPropertyChanged(nameof(ApplyResultDetailText));
        OnPropertyChanged(nameof(ValidationApprovalStatusText));
        OnPropertyChanged(nameof(ValidationResultText));
        OnPropertyChanged(nameof(ValidationTemplateStatusText));
        OnPropertyChanged(nameof(ValidationRunDetailText));
        OnPropertyChanged(nameof(RollbackStatusText));
        OnPropertyChanged(nameof(RollbackAvailabilityText));
        OnPropertyChanged(nameof(HasRollbackAvailable));
        OnPropertyChanged(nameof(CanShowRollback));
        OnPropertyChanged(nameof(RecentSessions));
        OnPropertyChanged(nameof(ProposalSummaries));
        OnPropertyChanged(nameof(ApplySummaries));
        OnPropertyChanged(nameof(ValidationSummaries));
        OnPropertyChanged(nameof(TimelineSummaries));
        OnPropertyChanged(nameof(HasWorkspaces));
        OnPropertyChanged(nameof(HasNoWorkspaces));
        OnPropertyChanged(nameof(HasProposals));
        OnPropertyChanged(nameof(HasNoProposals));
        OnPropertyChanged(nameof(HasProposal));
        OnPropertyChanged(nameof(HasNoSelectedProposal));
        OnPropertyChanged(nameof(HasProposalFiles));
        OnPropertyChanged(nameof(HasValidationTemplates));
        OnPropertyChanged(nameof(HasNoValidationTemplates));
        OnPropertyChanged(nameof(HasValidationRuns));
        OnPropertyChanged(nameof(HasNoValidationRuns));
        OnPropertyChanged(nameof(HasApplyResults));
        OnPropertyChanged(nameof(HasNoApplyResults));
        OnPropertyChanged(nameof(HasRecentCodeTasks));
        OnPropertyChanged(nameof(HasNoRecentCodeTasks));
        OnPropertyChanged(nameof(HasSelectedTaskTimeline));
        OnPropertyChanged(nameof(HasNoSelectedTaskTimeline));
        OnPropertyChanged(nameof(HasTimelineItems));
        OnPropertyChanged(nameof(HasNoTimelineItems));
        OnPropertyChanged(nameof(HasRecentSessions));
        OnPropertyChanged(nameof(HasNoRecentSessions));
        OnPropertyChanged(nameof(HasDashboard));
        OnPropertyChanged(nameof(HasProposalSummaries));
        OnPropertyChanged(nameof(HasNoProposalSummaries));
        OnPropertyChanged(nameof(HasValidationSummaries));
        OnPropertyChanged(nameof(HasNoValidationSummaries));
        OnPropertyChanged(nameof(HasApplySummaries));
        OnPropertyChanged(nameof(HasNoApplySummaries));
        OnPropertyChanged(nameof(HasTimelineSummaries));
        OnPropertyChanged(nameof(HasNoTimelineSummaries));
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        StartSessionCommand.NotifyCanExecuteChanged();
        CreateProposalCommand.NotifyCanExecuteChanged();
        SearchContextFilesCommand.NotifyCanExecuteChanged();
        AddContextFileCommand.NotifyCanExecuteChanged();
        CancelProposalCreationCommand.NotifyCanExecuteChanged();
        DryRunSelectedProposalCommand.NotifyCanExecuteChanged();
        RequestApplyApprovalCommand.NotifyCanExecuteChanged();
        AllowApplyOnceCommand.NotifyCanExecuteChanged();
        DenyApplyApprovalCommand.NotifyCanExecuteChanged();
        ApplyApprovedProposalCommand.NotifyCanExecuteChanged();
        CreateValidationRunCommand.NotifyCanExecuteChanged();
        RequestValidationApprovalCommand.NotifyCanExecuteChanged();
        AllowValidationOnceCommand.NotifyCanExecuteChanged();
        DenyValidationApprovalCommand.NotifyCanExecuteChanged();
        RunApprovedValidationCommand.NotifyCanExecuteChanged();
        RollbackSelectedApplyResultCommand.NotifyCanExecuteChanged();
    }
}

public sealed record AedaCodeWorkspaceItem(
    WorkspaceId WorkspaceId,
    string DisplayName,
    string RootSummary,
    string PolicyLabel,
    string SafeStatus)
{
    public static AedaCodeWorkspaceItem From(WorkspaceDescriptor workspace) =>
        new(
            workspace.Id,
            workspace.DisplayName,
            Path.GetFileName(workspace.CanonicalRootPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)),
            workspace.Policy.IsReadOnly ? "read-only" : "write approval required",
            "Registered workspace");
}

public sealed record AedaCodeProposalItem(
    PatchProposalId ProposalId,
    string Title,
    PatchProposalStatus Status,
    string RiskLabel,
    string RiskReason,
    int AffectedFileCount,
    string UpdatedText,
    string SafeSummary,
    AedaCodeProposalSummary Summary)
{
    public static AedaCodeProposalItem From(AedaCodeProposalSummary summary) =>
        new(
            summary.ProposalId,
            summary.Title,
            summary.Status,
            summary.Risk.Label,
            summary.Risk.SafeReason,
            summary.RelativePaths.Count,
            summary.UpdatedAtUtc.ToLocalTime().ToString("g"),
            string.Join(", ", summary.RelativePaths.Take(3)),
            summary);
}

public sealed record AedaCodeFileItem(
    string RelativePath,
    string ChangeKind,
    string OriginalHashStatus,
    string ProposedHashStatus)
{
    public static AedaCodeFileItem From(PatchProposalFile file) =>
        new(
            file.RelativePath.Replace('\\', '/'),
            file.ChangeKind.ToString(),
            string.IsNullOrWhiteSpace(file.OriginalContentHash) ? "missing original hash" : "original hash present",
            string.IsNullOrWhiteSpace(file.ProposedContentHash) ? "missing proposed hash" : "proposed hash present");
}

public sealed record AedaCodeApplyItem(
    PatchApplyResultId ApplyResultId,
    PatchProposalId ProposalId,
    PatchApplyStatus Status,
    int FileCount,
    string UpdatedText,
    AedaCodeApplySummary Summary)
{
    public string AccessibleSummary => $"{Status} · {UpdatedText}";

    public static AedaCodeApplyItem From(AedaCodeApplySummary summary) =>
        new(
            summary.ApplyResultId,
            summary.ProposalId,
            summary.Status,
            summary.FileCount,
            summary.UpdatedAtUtc.ToLocalTime().ToString("g"),
            summary);

    public static AedaCodeApplyItem From(PatchApplyResult result) =>
        From(new AedaCodeApplySummary(
            result.Id,
            result.ProposalId,
            result.Status,
            result.Files.Count,
            result.UpdatedAtUtc));
}

public sealed record AedaCodeValidationTemplateItem(
    string Id,
    string DisplayName,
    string SafeCommandSummary,
    string TimeoutText)
{
    public static AedaCodeValidationTemplateItem From(ValidationCommandTemplate template) =>
        new(
            template.Id,
            template.DisplayName,
            $"{template.Executable} {string.Join(" ", template.Arguments.Take(4))}",
            $"{template.Timeout.TotalMinutes:0.#} min");
}

public sealed record AedaCodeValidationRunItem(
    ValidationRunId RunId,
    string TemplateId,
    ValidationRunStatus Status,
    string LinkSummary,
    string UpdatedText,
    AedaCodeValidationSummary Summary)
{
    public static AedaCodeValidationRunItem From(AedaCodeValidationSummary summary) =>
        new(
            summary.RunId,
            summary.TemplateId,
            summary.Status,
            summary.ApplyResultId is not null
                ? "Linked to apply result"
                : summary.ProposalId is not null
                    ? "Linked to proposal"
                    : "Workspace validation",
            summary.UpdatedAtUtc.ToLocalTime().ToString("g"),
            summary);

    public static AedaCodeValidationRunItem From(ValidationRun run) =>
        From(new AedaCodeValidationSummary(
            run.Id,
            run.TemplateId,
            run.Status,
            run.ProposalId,
            run.ApplyResultId,
            run.UpdatedAtUtc));
}

public sealed record AedaCodeTimelineGroupItem(
    string Title,
    IReadOnlyList<AedaCodeTimelineEventItem> Items)
{
    public static AedaCodeTimelineGroupItem From(AedaTaskActivityGroup group) =>
        new(
            Redact(group.Title),
            group.Items
                .Take(6)
                .Select(AedaCodeTimelineEventItem.From)
                .ToArray());

    private static string Redact(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Code activity" : value.Trim();
}

public sealed record AedaCodeTimelineEventItem(
    string Label,
    string Detail,
    string UpdatedText)
{
    public static AedaCodeTimelineEventItem From(AedaTaskTimelineItem item)
    {
        var label = NormalizeLabel(item.Title, item.Summary);
        var detail = NormalizeDetail(item.Detail ?? item.Summary);
        return new(
            label,
            detail,
            item.TimestampUtc.ToLocalTime().ToString("g"));
    }

    private static string NormalizeLabel(string title, string summary)
    {
        var text = $"{title} {summary}";
        if (Contains(text, "context"))
        {
            return "Loaded safe context";
        }

        if (Contains(text, "proposal") && Contains(text, "created"))
        {
            return "Created proposal";
        }

        if (Contains(text, "proposal") && Contains(text, "validated"))
        {
            return "Validated proposal";
        }

        if (Contains(text, "dry run") && Contains(text, "passed"))
        {
            return "Dry run passed";
        }

        if (Contains(text, "dry run") && Contains(text, "staleoriginalcontent"))
        {
            return "Dry run blocked: proposal is stale";
        }

        if (Contains(text, "dry run"))
        {
            return "Dry run completed";
        }

        if (Contains(text, "approval") && Contains(text, "denied"))
        {
            return "Approval denied";
        }

        if (Contains(text, "approval") && Contains(text, "granted"))
        {
            return "Approval granted";
        }

        if (Contains(text, "approval"))
        {
            return "Waiting for approval";
        }

        if (Contains(text, "rollback") && Contains(text, "completed"))
        {
            return "Rollback completed";
        }

        if (Contains(text, "rollback"))
        {
            return "Rollback event";
        }

        if (Contains(text, "validation") && Contains(text, "failed"))
        {
            return "Validation failed";
        }

        if (Contains(text, "validation") && Contains(text, "succeeded"))
        {
            return "Validation passed";
        }

        if (Contains(text, "validation"))
        {
            return "Validation event";
        }

        if (Contains(text, "apply") && Contains(text, "completed"))
        {
            return "Applied proposal";
        }

        if (Contains(text, "apply"))
        {
            return "Apply event";
        }

        return string.IsNullOrWhiteSpace(title)
            ? "Code event"
            : title.Trim();
    }

    private static string NormalizeDetail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Safe event detail unavailable.";
        }

        var text = value.Trim();
        if (Contains(text, "raw payload") ||
            Contains(text, "full command output"))
        {
            return "Safe event detail withheld.";
        }

        text = Regex.Replace(text, @"[A-Za-z]:[\\/][^\s]+", match => Path.GetFileName(match.Value.TrimEnd(',', ';', ':')));
        text = Regex.Replace(text, @"(?i)(secret|password|token|api[_-]?key)\s*[:=]\s*[^\s,;]+", "$1=[redacted]");
        return text.Length <= 160 ? text : text[..160] + " [truncated]";
    }

    private static bool Contains(string value, string token) =>
        value.Contains(token, StringComparison.OrdinalIgnoreCase);
}
