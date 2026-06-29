using PersonalAI.Core.Approvals;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Coding;

public sealed class AedaCodeModuleService(
    IWorkspaceReader workspaceReader,
    ICodeContextService codeContextService,
    ICodeChangePlanningService planningService,
    ICodeProposalDraftService? draftService,
    IPatchProposalService proposalService,
    IPatchApplyService applyService,
    IValidationRunnerService validationRunnerService,
    IValidationCommandAllowlist validationCommandAllowlist,
    ITaskQueryService? taskQueryService = null,
    ITaskRuntime? taskRuntime = null) : IAedaCodeModuleService
{
    private const int MaxCreationRequestCharacters = 4_000;
    private const int MaxCreationTitleCharacters = 120;
    private const int MaxContextFiles = 8;
    private const int MaxContextCharactersPerFile = 30_000;
    private static readonly HashSet<string> PreferredContextExtensions = new(
        [".cs", ".xaml", ".csproj", ".props", ".json", ".md", ".ts", ".tsx", ".js", ".jsx", ".css"],
        StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly List<AedaCodeSession> _sessions = [];

    public Task<AedaCodeSession> StartSessionAsync(
        WorkspaceId workspaceId,
        string? safeSummary = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workspace = workspaceReader.GetWorkspace(workspaceId);
        var now = DateTimeOffset.UtcNow;
        var session = new AedaCodeSession(
            AedaCodeSessionId.NewId(),
            workspace.Id,
            workspace.DisplayName,
            CurrentTaskId: null,
            ActiveProposalId: null,
            ActiveApplyResultId: null,
            ActiveValidationRunId: null,
            now,
            now,
            AedaCodeSessionStatus.Active,
            string.IsNullOrWhiteSpace(safeSummary)
                ? "AEDA Code session"
                : safeSummary.Trim());

        lock (_gate)
        {
            _sessions.Insert(0, session);
        }

        return Task.FromResult(session);
    }

    public Task<IReadOnlyList<AedaCodeSession>> ListRecentSessionsAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<AedaCodeSession>>(
                _sessions
                    .OrderByDescending(session => session.UpdatedAtUtc)
                    .Take(Math.Max(0, limit))
                    .ToArray());
        }
    }

    public Task<AedaCodeWorkspaceSummary> GetWorkspaceSummaryAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workspace = workspaceReader.GetWorkspace(workspaceId);
        int? fileCount = null;
        int? directoryCount = null;

        try
        {
            var entries = workspaceReader.ListDirectory(
                workspaceId,
                ".",
                maxEntries: 500,
                includeHidden: false,
                cancellationToken);
            fileCount = entries.Count(entry => entry.Type == WorkspaceEntryType.File);
            directoryCount = entries.Count(entry => entry.Type == WorkspaceEntryType.Directory);
        }
        catch (Exception)
        {
            fileCount = null;
            directoryCount = null;
        }

        return Task.FromResult(
            new AedaCodeWorkspaceSummary(
                workspace.Id,
                workspace.DisplayName,
                workspace.Policy.IsReadOnly,
                fileCount,
                directoryCount));
    }

    public Task<CodeContextPack> ReadFilesAsync(
        WorkspaceId workspaceId,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken = default) =>
        codeContextService.LoadFilesAsync(workspaceId, relativePaths, cancellationToken: cancellationToken);

    public Task<CodeContextPack> SearchAsync(
        CodeContextSearchRequest request,
        CancellationToken cancellationToken = default) =>
        codeContextService.SearchAsync(request, cancellationToken);

    public Task<CodeChangePlan> CreatePlanAsync(
        CodeChangeRequest request,
        CodeContextPack context,
        CancellationToken cancellationToken = default) =>
        planningService.CreatePlanAsync(request, context, cancellationToken);

    public Task<PatchProposal> CreateProposalAsync(
        PatchProposalCreateRequest request,
        CancellationToken cancellationToken = default) =>
        proposalService.CreateProposalAsync(request, cancellationToken);

    public async Task<AedaCodeProposalCreationResult> CreateProposalFromRequestAsync(
        AedaCodeProposalCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (draftService is null)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.ProviderUnavailable);
        }

        if (string.IsNullOrWhiteSpace(request.UserRequest))
        {
            throw Failure(AedaCodeProposalCreationFailureReason.RequestEmpty);
        }

        if (request.UserRequest.Length > MaxCreationRequestCharacters)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.RequestTooLong);
        }

        if (request.OptionalTitle?.Length > MaxCreationTitleCharacters)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.RequestTooLong);
        }

        WorkspaceDescriptor workspace;
        try
        {
            workspace = workspaceReader.GetWorkspace(request.WorkspaceId);
        }
        catch (WorkspaceAccessException exception)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.WorkspaceUnavailable, exception);
        }
        catch (InvalidOperationException exception)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.WorkspaceMissing, exception);
        }

        var task = taskRuntime is null
            ? null
            : await taskRuntime.StartTaskAsync(
                CreateTaskTitle(request),
                "aeda-code",
                model: null,
                provider: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

        try
        {
            await AppendTaskAsync(task?.Id, TaskEventKind.CodeChangeRequested, "Code proposal creation requested.", cancellationToken)
                .ConfigureAwait(false);
            CodeContextPack context;
            try
            {
                context = await GatherBoundedContextAsync(
                    workspace.Id,
                    request.UserRequest,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (WorkspaceAccessException exception)
            {
                throw Failure(AedaCodeProposalCreationFailureReason.WorkspaceUnavailable, exception);
            }

            await AppendTaskAsync(task?.Id, TaskEventKind.CodeContextLoaded, "Bounded code context loaded.", cancellationToken)
                .ConfigureAwait(false);

            var changeRequest = CodeChangeRequest.Create(
                workspace.Id,
                request.UserRequest,
                context.Files.Select(file => file.RelativePath).ToArray(),
                "aeda-code-ui");
            var plan = await planningService.CreatePlanAsync(
                changeRequest,
                context,
                cancellationToken).ConfigureAwait(false);
            await AppendTaskAsync(task?.Id, TaskEventKind.CodeChangePlanCreated, "Code change plan created.", cancellationToken)
                .ConfigureAwait(false);

            var draft = await draftService.CreateDraftAsync(
                new CodeProposalDraftRequest(
                    changeRequest,
                    context,
                    request.OptionalTitle),
                cancellationToken).ConfigureAwait(false);

            PatchProposal proposal;
            try
            {
                proposal = await proposalService.CreateProposalAsync(
                    new PatchProposalCreateRequest(
                        workspace.Id,
                        string.IsNullOrWhiteSpace(request.OptionalTitle)
                            ? draft.Title
                            : request.OptionalTitle.Trim(),
                        draft.Summary,
                        draft.FileEdits,
                        plan.ContextSources,
                        plan.ValidationPlan),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw Failure(AedaCodeProposalCreationFailureReason.ProposalValidationFailed, exception);
            }
            catch (IOException exception)
            {
                throw Failure(AedaCodeProposalCreationFailureReason.ProposalPersistenceFailed, exception);
            }

            await AppendTaskAsync(task?.Id, TaskEventKind.PatchProposalCreated, "Patch proposal created for review.", cancellationToken)
                .ConfigureAwait(false);
            if (taskRuntime is not null && task is not null)
            {
                await taskRuntime.AttachArtifactAsync(
                    task.Id,
                    TaskArtifact.Create(
                        proposal.Title,
                        "patch-proposal",
                        proposal.Id.ToString()),
                    cancellationToken).ConfigureAwait(false);
                await taskRuntime.CompleteTaskAsync(task.Id, cancellationToken).ConfigureAwait(false);
            }

            return new AedaCodeProposalCreationResult(
                proposal,
                ToProposalSummary(proposal),
                context.Files.Select(file => file.RelativePath).ToArray(),
                draft.SafeNotices);
        }
        catch (Exception exception)
        {
            var failure = ToFailure(exception);
            if (taskRuntime is not null && task is not null)
            {
                await taskRuntime.FailTaskAsync(
                    task.Id,
                    failure.SafeCode,
                    CancellationToken.None).ConfigureAwait(false);
            }

            throw exception is AedaCodeProposalCreationException
                ? exception
                : new AedaCodeProposalCreationException(failure, exception);
        }
    }

    public Task<PatchProposal?> GetProposalAsync(
        PatchProposalId proposalId,
        CancellationToken cancellationToken = default) =>
        proposalService.GetProposalAsync(proposalId, cancellationToken);

    public async Task<IReadOnlyList<AedaCodeProposalSummary>> ListProposalSummariesAsync(
        WorkspaceId workspaceId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var proposals = await proposalService.ListRecentProposalsAsync(
            Math.Max(limit * 2, limit),
            cancellationToken).ConfigureAwait(false);

        return proposals
            .Where(proposal => proposal.WorkspaceId == workspaceId)
            .OrderByDescending(proposal => proposal.UpdatedAtUtc)
            .Take(Math.Max(0, limit))
            .Select(ToProposalSummary)
            .ToArray();
    }

    public Task<PatchApplyPlan> DryRunApplyAsync(
        PatchApplyRequest request,
        CancellationToken cancellationToken = default) =>
        applyService.DryRunAsync(request, cancellationToken);

    public Task<IReadOnlyList<ValidationCommandTemplate>> ListValidationTemplatesAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workspace = workspaceReader.GetWorkspace(workspaceId);
        var templates = validationCommandAllowlist.ListTemplates()
            .Where(template => validationCommandAllowlist.TryCreateCommand(
                new ValidationRunRequest(workspace.Id, template.Id),
                workspace,
                out _,
                out _))
            .OrderBy(template => template.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(template => template.Id, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ValidationCommandTemplate>>(templates);
    }

    public Task<ApprovalRequest> RequestApplyApprovalAsync(
        PatchProposalId proposalId,
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default) =>
        applyService.RequestApplyApprovalAsync(proposalId, workspaceId, cancellationToken);

    public Task<PatchApplyResult> ApplyApprovedProposalAsync(
        PatchApplyRequest request,
        CancellationToken cancellationToken = default) =>
        applyService.ApplyAsync(request, cancellationToken);

    public Task<PatchApplyResult?> GetApplyResultAsync(
        PatchApplyResultId applyResultId,
        CancellationToken cancellationToken = default) =>
        applyService.GetApplyResultAsync(applyResultId, cancellationToken);

    public Task<PatchRollbackResult> RollbackAsync(
        PatchRollbackRequest request,
        CancellationToken cancellationToken = default) =>
        applyService.RollbackAsync(request, cancellationToken);

    public Task<ValidationRun> CreateValidationRunAsync(
        ValidationRunRequest request,
        CancellationToken cancellationToken = default) =>
        validationRunnerService.CreateRunAsync(request, cancellationToken);

    public Task<ApprovalRequest> RequestValidationApprovalAsync(
        ValidationRunId runId,
        CancellationToken cancellationToken = default) =>
        validationRunnerService.RequestApprovalAsync(runId, cancellationToken);

    public Task<ValidationRun> RunApprovedValidationAsync(
        ValidationRunId runId,
        ApprovalRequest approvalRequest,
        ApprovalDecision approvalDecision,
        CancellationToken cancellationToken = default) =>
        validationRunnerService.ExecuteAsync(
            runId,
            approvalRequest,
            approvalDecision,
            cancellationToken);

    public async Task<AedaCodeDashboardModel> GetDashboardAsync(
        AedaCodeSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = GetSession(sessionId);
        var workspace = await GetWorkspaceSummaryAsync(
            session.WorkspaceId,
            cancellationToken).ConfigureAwait(false);
        var proposals = await ListProposalSummariesAsync(
            session.WorkspaceId,
            limit: 20,
            cancellationToken).ConfigureAwait(false);
        var applyResults = await applyService.ListRecentApplyResultsAsync(
            limit: 50,
            cancellationToken).ConfigureAwait(false);
        var validations = await validationRunnerService.ListRecentAsync(
            session.WorkspaceId,
            limit: 50,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var timeline = await BuildTimelineAsync(cancellationToken).ConfigureAwait(false);

        return new AedaCodeDashboardModel(
            session,
            workspace,
            proposals,
            applyResults
                .Where(result => result.WorkspaceId == session.WorkspaceId)
                .OrderByDescending(result => result.UpdatedAtUtc)
                .Take(20)
                .Select(ToApplySummary)
                .ToArray(),
            validations
                .OrderByDescending(run => run.UpdatedAtUtc)
                .Take(20)
                .Select(ToValidationSummary)
                .ToArray(),
            timeline);
    }

    private AedaCodeSession GetSession(AedaCodeSessionId sessionId)
    {
        lock (_gate)
        {
            return _sessions.FirstOrDefault(session => session.Id == sessionId)
                ?? throw new InvalidOperationException("aeda_code_session_not_found");
        }
    }

    private async Task<IReadOnlyList<AedaCodeTimelineItem>> BuildTimelineAsync(
        CancellationToken cancellationToken)
    {
        if (taskQueryService is null)
        {
            return [];
        }

        var taskRuns = await taskQueryService.ListRecentTaskRunsAsync(
            20,
            cancellationToken).ConfigureAwait(false);

        return taskRuns
            .OrderByDescending(run => run.UpdatedAtUtc)
            .Select(run => new AedaCodeTimelineItem(
                run.UpdatedAtUtc,
                "task",
                $"{run.Title} ({run.Status})"))
            .ToArray();
    }

    private async Task<CodeContextPack> GatherBoundedContextAsync(
        WorkspaceId workspaceId,
        string userRequest,
        CancellationToken cancellationToken)
    {
        var paths = DiscoverContextPaths(workspaceId, cancellationToken);
        if (paths.Count == 0)
        {
            var query = CreateSearchQuery(userRequest);
            if (!string.IsNullOrWhiteSpace(query))
            {
                var search = await codeContextService.SearchAsync(
                    new CodeContextSearchRequest(
                        workspaceId,
                        query,
                        ".",
                        null,
                        MaxResults: MaxContextFiles),
                    cancellationToken).ConfigureAwait(false);
                paths = search.SearchMatches
                    .Select(match => match.RelativePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(MaxContextFiles)
                    .ToArray();
            }
        }

        var context = await codeContextService.LoadFilesAsync(
            workspaceId,
            paths,
            MaxContextFiles,
            MaxContextCharactersPerFile,
            cancellationToken).ConfigureAwait(false);
        if (context.Files.Count == 0)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.NoSafeContext);
        }

        return context;
    }

    private IReadOnlyList<string> DiscoverContextPaths(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        var paths = new List<string>();
        AddPreferredFiles(workspaceId, ".", paths, cancellationToken);
        foreach (var directory in workspaceReader.ListDirectory(
                     workspaceId,
                     ".",
                     maxEntries: 100,
                     includeHidden: false,
                     cancellationToken)
                     .Where(entry => entry.Type == WorkspaceEntryType.Directory)
                     .Select(entry => entry.RelativePath)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(12))
        {
            if (paths.Count >= MaxContextFiles)
            {
                break;
            }

            AddPreferredFiles(workspaceId, directory, paths, cancellationToken);
        }

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxContextFiles)
            .ToArray();
    }

    private void AddPreferredFiles(
        WorkspaceId workspaceId,
        string relativeDirectory,
        List<string> paths,
        CancellationToken cancellationToken)
    {
        if (paths.Count >= MaxContextFiles)
        {
            return;
        }

        IReadOnlyList<WorkspaceDirectoryEntry> entries;
        try
        {
            entries = workspaceReader.ListDirectory(
                workspaceId,
                relativeDirectory,
                maxEntries: 120,
                includeHidden: false,
                cancellationToken);
        }
        catch (WorkspaceAccessException)
        {
            return;
        }

        foreach (var entry in entries
                     .Where(entry => entry.Type == WorkspaceEntryType.File)
                     .Where(entry => PreferredContextExtensions.Contains(entry.Extension))
                     .OrderBy(entry => ContextPathPriority(entry.RelativePath))
                     .ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            paths.Add(entry.RelativePath.Replace('\\', '/'));
            if (paths.Count >= MaxContextFiles)
            {
                return;
            }
        }
    }

    private static int ContextPathPriority(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    private static string CreateSearchQuery(string userRequest)
    {
        var token = userRequest
            .Split([' ', '\r', '\n', '\t', '.', ',', ';', ':', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.Length >= 4)
            .FirstOrDefault();
        return token ?? string.Empty;
    }

    private async ValueTask AppendTaskAsync(
        TaskId? taskId,
        TaskEventKind kind,
        string summary,
        CancellationToken cancellationToken)
    {
        if (taskRuntime is null || taskId is null)
        {
            return;
        }

        await taskRuntime.AppendEventAsync(taskId.Value, kind, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string CreateTaskTitle(AedaCodeProposalCreationRequest request)
    {
        var title = string.IsNullOrWhiteSpace(request.OptionalTitle)
            ? request.UserRequest
            : request.OptionalTitle!;
        title = string.IsNullOrWhiteSpace(title) ? "Create code proposal" : title.Trim();
        return title.Length <= 80 ? title : title[..80];
    }

    private static AedaCodeProposalCreationException Failure(
        AedaCodeProposalCreationFailureReason reason,
        Exception? innerException = null) =>
        new(AedaCodeProposalCreationFailure.FromReason(reason), innerException);

    private static AedaCodeProposalCreationFailure ToFailure(Exception exception)
    {
        if (exception is AedaCodeProposalCreationException proposalException)
        {
            return proposalException.Failure;
        }

        if (exception is OperationCanceledException)
        {
            return AedaCodeProposalCreationFailure.FromReason(
                AedaCodeProposalCreationFailureReason.ModelCancelled);
        }

        if (exception is TimeoutException)
        {
            return AedaCodeProposalCreationFailure.FromReason(
                AedaCodeProposalCreationFailureReason.ModelTimeout);
        }

        if (exception is WorkspaceAccessException)
        {
            return AedaCodeProposalCreationFailure.FromReason(
                AedaCodeProposalCreationFailureReason.WorkspaceUnavailable);
        }

        if (exception is IOException)
        {
            return AedaCodeProposalCreationFailure.FromReason(
                AedaCodeProposalCreationFailureReason.ProposalPersistenceFailed);
        }

        return AedaCodeProposalCreationFailure.FromReason(
            AedaCodeProposalCreationFailureReason.UnknownSafeFailure);
    }

    private static AedaCodeProposalSummary ToProposalSummary(PatchProposal proposal) =>
        new(
            proposal.Id,
            proposal.Title,
            proposal.Status,
            ToRiskBadge(proposal.Risk, proposal.RiskReasons),
            proposal.Files.Select(file => file.RelativePath).ToArray(),
            proposal.UpdatedAtUtc);

    private static AedaCodeRiskBadge ToRiskBadge(
        PatchProposalRisk risk,
        IReadOnlyList<string> reasons) =>
        new(
            risk,
            risk.ToString(),
            reasons.Count == 0
                ? "risk_not_classified"
                : string.Join(", ", reasons.Take(3)));

    private static AedaCodeApplySummary ToApplySummary(PatchApplyResult result) =>
        new(
            result.Id,
            result.ProposalId,
            result.Status,
            result.Files.Count,
            result.UpdatedAtUtc);

    private static AedaCodeValidationSummary ToValidationSummary(ValidationRun run) =>
        new(
            run.Id,
            run.TemplateId,
            run.Status,
            run.ProposalId,
            run.ApplyResultId,
            run.UpdatedAtUtc);
}
