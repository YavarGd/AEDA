using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAI.Core.Approvals;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class AedaCodeModuleViewModel : ObservableObject
{
    private const int SummaryLimit = 6;
    private const int DetailFileLimit = 20;
    private const int DiffLineLimit = 900;
    private const int DiffCharacterLimit = 60_000;
    private const int ValidationOutputLimit = 3_000;
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

    public ObservableCollection<AedaCodeApplyItem> ApplyResults { get; } = [];

    public ObservableCollection<AedaCodeValidationTemplateItem> ValidationTemplates { get; } = [];

    public ObservableCollection<AedaCodeValidationRunItem> ValidationRuns { get; } = [];

    public ObservableCollection<AedaTaskSummary> RecentCodeTasks { get; } = [];

    public ObservableCollection<AedaTaskActivityGroup> SelectedTaskTimeline { get; } = [];

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

    public string ValidationPlanText { get; private set; } = "Select a proposal to view validation guidance.";

    public string HashStatusText { get; private set; } = "No proposal selected.";

    public string SourceSummaryText { get; private set; } = "No source context loaded.";

    public string DryRunStatusText => DryRunPlan is null
        ? "Dry run not started."
        : $"{DryRunPlan.Status} · {DryRunPlan.Operations.Count} operation(s)";

    public string ApplyApprovalStatusText => ApplyApprovalRequest is null
        ? "Apply approval not requested."
        : ApplyApprovalDecision is null
            ? "Apply approval requested."
            : $"Apply approval {ApplyApprovalDecision.Kind}.";

    public string ApplyResultText => ApplyResult is null
        ? "No apply result."
        : $"{ApplyResult.Status} · {ApplyResult.Files.Count} file result(s)";

    public string ValidationApprovalStatusText => ValidationApprovalRequest is null
        ? "Validation approval not requested."
        : ValidationApprovalDecision is null
            ? "Validation approval requested."
            : $"Validation approval {ValidationApprovalDecision.Kind}.";

    public string ValidationResultText => ValidationRun is null
        ? "No validation run selected."
        : $"{ValidationRun.Status} · {ValidationRun.TemplateId}";

    public string RollbackStatusText => RollbackResult is null
        ? "Rollback not run."
        : $"{RollbackResult.Status} · {RollbackResult.Files.Count} file result(s)";

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

    public bool CanShowRollback => ApplyResult is { Status: PatchApplyStatus.Applied or PatchApplyStatus.PartiallyApplied };

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
    private string _safeStatusMessage = "AEDA Code ready.";

    [ObservableProperty]
    private bool _isBusy;

    private PatchApplyPlan? DryRunPlan { get; set; }

    private ApprovalRequest? ApplyApprovalRequest { get; set; }

    private ApprovalDecision? ApplyApprovalDecision { get; set; }

    private PatchApplyResult? ApplyResult { get; set; }

    private ValidationRun? ValidationRun { get; set; }

    private ApprovalRequest? ValidationApprovalRequest { get; set; }

    private ApprovalDecision? ValidationApprovalDecision { get; set; }

    private PatchRollbackResult? RollbackResult { get; set; }

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
                cancellationToken).ConfigureAwait(false);
            await LoadRecentCodeTasksAsync(cancellationToken).ConfigureAwait(false);
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

        try
        {
            IsBusy = true;
            Session = await _moduleService.StartSessionAsync(
                SelectedWorkspace.WorkspaceId,
                "Supervised Code workflow",
                cancellationToken).ConfigureAwait(false);
            await RefreshDashboardAsync(cancellationToken).ConfigureAwait(false);
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
        if (Session is not null)
        {
            await RefreshDashboardAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (SelectedWorkspace is not null)
        {
            await LoadWorkspaceWorkflowAsync(cancellationToken).ConfigureAwait(false);
        }

        await LoadRecentCodeTasksAsync(cancellationToken).ConfigureAwait(false);
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

        Dashboard = await _moduleService.GetDashboardAsync(
            Session.Id,
            cancellationToken).ConfigureAwait(false);
        ApplyDashboard(Dashboard);
        SafeStatusMessage = "AEDA Code dashboard refreshed.";
    }

    [RelayCommand]
    public async Task SelectWorkspaceAsync(
        AedaCodeWorkspaceItem? workspace,
        CancellationToken cancellationToken = default)
    {
        SelectedWorkspace = workspace;
        ClearProposalDetail();
        if (workspace is null)
        {
            SafeStatusMessage = "Select a registered workspace.";
            NotifyAll();
            return;
        }

        await LoadWorkspaceWorkflowAsync(cancellationToken).ConfigureAwait(false);
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

        try
        {
            var detail = await _moduleService.GetProposalAsync(
                proposal.ProposalId,
                cancellationToken).ConfigureAwait(false);
            if (detail is null)
            {
                SafeStatusMessage = "Selected proposal is no longer available.";
                ClearProposalDetail();
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
            SafeStatusMessage = "Proposal load cancelled.";
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            SafeStatusMessage = "Proposal detail is temporarily unavailable.";
            ClearProposalDetail();
        }
        finally
        {
            NotifyAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateProposal))]
    public Task CreateProposalAsync()
    {
        SafeStatusMessage = "Proposal creation is available only through existing safe backend paths.";
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanDryRun))]
    public async Task DryRunSelectedProposalAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProposal is null || SelectedWorkspace is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            DryRunPlan = await _moduleService.DryRunApplyAsync(
                new PatchApplyRequest(
                    SelectedProposal.ProposalId,
                    SelectedWorkspace.WorkspaceId),
                cancellationToken).ConfigureAwait(false);
            SafeStatusMessage = DryRunPlan.Status == PatchApplyStatus.DryRunPassed
                ? "Dry run passed."
                : "Dry run completed with safe blockers.";
        }
        catch (OperationCanceledException)
        {
            SafeStatusMessage = "Dry run cancelled.";
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            SafeStatusMessage = "Dry run failed safely.";
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

        try
        {
            ApplyApprovalRequest = await _moduleService.RequestApplyApprovalAsync(
                SelectedProposal.ProposalId,
                SelectedWorkspace.WorkspaceId,
                cancellationToken).ConfigureAwait(false);
            ApplyApprovalDecision = null;
            SafeStatusMessage = "Apply approval requested.";
        }
        catch (OperationCanceledException)
        {
            SafeStatusMessage = "Apply approval request cancelled.";
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            SafeStatusMessage = "Could not request apply approval safely.";
        }
        finally
        {
            NotifyAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDecideApplyApproval))]
    public async Task AllowApplyOnceAsync(CancellationToken cancellationToken = default)
    {
        if (ApplyApprovalRequest is null)
        {
            return;
        }

        ApplyApprovalDecision = await _approvalStore.DecideAsync(
            ApplyApprovalRequest,
            ApprovalDecisionKind.AllowOnce,
            "Allowed from AEDA Code workflow.",
            cancellationToken).ConfigureAwait(false);
        SafeStatusMessage = "Apply approved once.";
        NotifyAll();
    }

    [RelayCommand(CanExecute = nameof(CanDecideApplyApproval))]
    public async Task DenyApplyApprovalAsync(CancellationToken cancellationToken = default)
    {
        if (ApplyApprovalRequest is null)
        {
            return;
        }

        ApplyApprovalDecision = await _approvalStore.DecideAsync(
            ApplyApprovalRequest,
            ApprovalDecisionKind.Deny,
            "Denied from AEDA Code workflow.",
            cancellationToken).ConfigureAwait(false);
        SafeStatusMessage = "Apply approval denied.";
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

        try
        {
            IsBusy = true;
            ApplyResult = await _moduleService.ApplyApprovedProposalAsync(
                new PatchApplyRequest(
                    SelectedProposal.ProposalId,
                    SelectedWorkspace.WorkspaceId,
                    ApplyApprovalRequest,
                    ApplyApprovalDecision),
                cancellationToken).ConfigureAwait(false);
            AddOrUpdateApplyResult(ApplyResult);
            SafeStatusMessage = ApplyResult.Status == PatchApplyStatus.Applied
                ? "Proposal applied."
                : "Apply completed with safe blockers.";
        }
        catch (OperationCanceledException)
        {
            SafeStatusMessage = "Apply cancelled.";
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            SafeStatusMessage = "Apply failed safely.";
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

        try
        {
            ValidationRun = await _moduleService.CreateValidationRunAsync(
                new ValidationRunRequest(
                    SelectedWorkspace.WorkspaceId,
                    SelectedValidationTemplate.Id,
                    ".",
                    SelectedProposal?.ProposalId,
                    ApplyResult?.Id),
                cancellationToken).ConfigureAwait(false);
            AddOrUpdateValidationRun(ValidationRun);
            ValidationApprovalRequest = null;
            ValidationApprovalDecision = null;
            ValidationOutputPreview = "Validation run created. Request approval before running it.";
            SafeStatusMessage = "Validation run created.";
        }
        catch (OperationCanceledException)
        {
            SafeStatusMessage = "Validation run creation cancelled.";
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            SafeStatusMessage = "Validation run could not be created safely.";
        }
        finally
        {
            NotifyAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRequestValidationApproval))]
    public async Task RequestValidationApprovalAsync(CancellationToken cancellationToken = default)
    {
        if (ValidationRun is null)
        {
            return;
        }

        try
        {
            ValidationApprovalRequest = await _moduleService.RequestValidationApprovalAsync(
                ValidationRun.Id,
                cancellationToken).ConfigureAwait(false);
            ValidationApprovalDecision = null;
            SafeStatusMessage = "Validation approval requested.";
        }
        catch (OperationCanceledException)
        {
            SafeStatusMessage = "Validation approval request cancelled.";
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            SafeStatusMessage = "Could not request validation approval safely.";
        }
        finally
        {
            NotifyAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDecideValidationApproval))]
    public async Task AllowValidationOnceAsync(CancellationToken cancellationToken = default)
    {
        if (ValidationApprovalRequest is null)
        {
            return;
        }

        ValidationApprovalDecision = await _approvalStore.DecideAsync(
            ValidationApprovalRequest,
            ApprovalDecisionKind.AllowOnce,
            "Allowed from AEDA Code workflow.",
            cancellationToken).ConfigureAwait(false);
        SafeStatusMessage = "Validation approved once.";
        NotifyAll();
    }

    [RelayCommand(CanExecute = nameof(CanDecideValidationApproval))]
    public async Task DenyValidationApprovalAsync(CancellationToken cancellationToken = default)
    {
        if (ValidationApprovalRequest is null)
        {
            return;
        }

        ValidationApprovalDecision = await _approvalStore.DecideAsync(
            ValidationApprovalRequest,
            ApprovalDecisionKind.Deny,
            "Denied from AEDA Code workflow.",
            cancellationToken).ConfigureAwait(false);
        SafeStatusMessage = "Validation approval denied.";
        NotifyAll();
    }

    [RelayCommand(CanExecute = nameof(CanRunApprovedValidation))]
    public async Task RunApprovedValidationAsync(CancellationToken cancellationToken = default)
    {
        if (ValidationRun is null ||
            ValidationApprovalRequest is null ||
            ValidationApprovalDecision is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ValidationRun = await _moduleService.RunApprovedValidationAsync(
                ValidationRun.Id,
                ValidationApprovalRequest,
                ValidationApprovalDecision,
                cancellationToken).ConfigureAwait(false);
            AddOrUpdateValidationRun(ValidationRun);
            ValidationOutputPreview = BuildValidationOutput(ValidationRun);
            SafeStatusMessage = "Validation run completed.";
        }
        catch (OperationCanceledException)
        {
            SafeStatusMessage = "Validation run cancelled.";
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            SafeStatusMessage = "Validation failed safely.";
        }
        finally
        {
            IsBusy = false;
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

        try
        {
            IsBusy = true;
            RollbackResult = await _moduleService.RollbackAsync(
                new PatchRollbackRequest(
                    ApplyResult.Id,
                    SelectedWorkspace.WorkspaceId),
                cancellationToken).ConfigureAwait(false);
            SafeStatusMessage = RollbackResult.Status == PatchApplyStatus.RolledBack
                ? "Rollback completed."
                : "Rollback completed with safe blockers.";
        }
        catch (OperationCanceledException)
        {
            SafeStatusMessage = "Rollback cancelled.";
        }
        catch (Exception exception) when (IsSafeFailure(exception))
        {
            SafeStatusMessage = "Rollback failed safely.";
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
        if (task is null)
        {
            SafeStatusMessage = "Select a Code task to view its timeline.";
            NotifyAll();
            return;
        }

        var groups = await _taskCenterService.GetTimelineAsync(
            task.Id,
            SummaryLimit,
            cancellationToken).ConfigureAwait(false);
        foreach (var group in groups)
        {
            SelectedTaskTimeline.Add(group);
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

    private bool CanCreateProposal() => false;

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
        ApplyApprovalRequest is not null &&
        ApplyApprovalDecision?.IsAllowed == true;

    private bool CanCreateValidationRun() =>
        !IsBusy &&
        SelectedWorkspace is not null &&
        SelectedValidationTemplate is not null &&
        (SelectedProposal is not null || ApplyResult is not null);

    private bool CanRequestValidationApproval() =>
        !IsBusy &&
        ValidationRun is { Status: ValidationRunStatus.Created };

    private bool CanDecideValidationApproval() =>
        !IsBusy &&
        ValidationApprovalRequest is not null &&
        ValidationApprovalDecision is null;

    private bool CanRunApprovedValidation() =>
        !IsBusy &&
        ValidationRun is not null &&
        ValidationApprovalRequest is not null &&
        ValidationApprovalDecision?.IsAllowed == true;

    private bool CanRollback() => !IsBusy && CanShowRollback && SelectedWorkspace is not null;

    partial void OnIsBusyChanged(bool value) => NotifyCommandStates();

    partial void OnSelectedWorkspaceChanged(AedaCodeWorkspaceItem? value) => NotifyCommandStates();

    partial void OnSelectedProposalChanged(AedaCodeProposalItem? value) => NotifyCommandStates();

    partial void OnSelectedValidationTemplateChanged(AedaCodeValidationTemplateItem? value) => NotifyCommandStates();

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

    private async Task LoadWorkspaceWorkflowAsync(CancellationToken cancellationToken)
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        var proposals = await _moduleService.ListProposalSummariesAsync(
            SelectedWorkspace.WorkspaceId,
            SummaryLimit,
            cancellationToken).ConfigureAwait(false);
        Proposals.Clear();
        foreach (var proposal in proposals
                     .OrderByDescending(proposal => proposal.UpdatedAtUtc)
                     .ThenBy(proposal => proposal.ProposalId.ToString(), StringComparer.Ordinal))
        {
            Proposals.Add(AedaCodeProposalItem.From(proposal));
        }

        ValidationTemplates.Clear();
        var templates = await _moduleService.ListValidationTemplatesAsync(
            SelectedWorkspace.WorkspaceId,
            cancellationToken).ConfigureAwait(false);
        foreach (var template in templates)
        {
            ValidationTemplates.Add(AedaCodeValidationTemplateItem.From(template));
        }

        SelectedValidationTemplate = ValidationTemplates.FirstOrDefault();
    }

    private async Task LoadRecentCodeTasksAsync(CancellationToken cancellationToken)
    {
        RecentCodeTasks.Clear();
        var tasks = await _taskCenterService.ListTasksByModuleAsync(
            AedaTaskCenterModule.Code,
            SummaryLimit,
            cancellationToken).ConfigureAwait(false);
        foreach (var task in tasks)
        {
            RecentCodeTasks.Add(task);
        }

        SelectedTask ??= RecentCodeTasks.FirstOrDefault();
        if (SelectedTask is not null && SelectedTaskTimeline.Count == 0)
        {
            await SelectTaskAsync(SelectedTask, cancellationToken).ConfigureAwait(false);
        }
    }

    private void ApplyDashboard(AedaCodeDashboardModel dashboard)
    {
        SelectedWorkspace = Workspaces.FirstOrDefault(workspace =>
            workspace.WorkspaceId == dashboard.Workspace.WorkspaceId)
            ?? SelectedWorkspace;
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
        ApplyApprovalRequest = null;
        ApplyApprovalDecision = null;
        ApplyResult = null;
        ValidationRun = null;
        ValidationApprovalRequest = null;
        ValidationApprovalDecision = null;
        RollbackResult = null;
        ValidationOutputPreview = "Run an approved validation to view sanitized output.";
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
            .Take(6);
        return string.Join(", ", files);
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

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(WorkspaceSummary));
        OnPropertyChanged(nameof(SessionStatusText));
        OnPropertyChanged(nameof(RecentCodeTaskCountText));
        OnPropertyChanged(nameof(ProposalCountText));
        OnPropertyChanged(nameof(AffectedFileCountText));
        OnPropertyChanged(nameof(RiskSummary));
        OnPropertyChanged(nameof(ValidationPlanText));
        OnPropertyChanged(nameof(HashStatusText));
        OnPropertyChanged(nameof(SourceSummaryText));
        OnPropertyChanged(nameof(DryRunStatusText));
        OnPropertyChanged(nameof(ApplyApprovalStatusText));
        OnPropertyChanged(nameof(ApplyResultText));
        OnPropertyChanged(nameof(ValidationApprovalStatusText));
        OnPropertyChanged(nameof(ValidationResultText));
        OnPropertyChanged(nameof(RollbackStatusText));
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
