using System.Collections.Concurrent;
using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Tools;

public sealed class TypedToolRuntime : ITypedToolRuntime
{
    private readonly IToolRegistry _registry;
    private readonly ITaskEventBus _eventBus;
    private readonly IPermissionBroker _permissionBroker;
    private readonly ToolRuntimeOptions _options;
    private readonly IToolRuntimeLogger _logger;
    private readonly ConcurrentDictionary<PermissionGrantKey, PermissionResponse>
        _allowedForTask = [];

    public TypedToolRuntime(
        IToolRegistry registry,
        ITaskEventBus eventBus,
        IPermissionBroker permissionBroker,
        ToolRuntimeOptions? options = null,
        IToolRuntimeLogger? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _permissionBroker = permissionBroker ??
            throw new ArgumentNullException(nameof(permissionBroker));
        _options = options ?? ToolRuntimeOptions.Default;
        _logger = logger ?? NullToolRuntimeLogger.Instance;

        if (_options.DefaultTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Default timeout must be positive.");
        }
    }

    public async ValueTask<ToolResult> InvokeAsync(
        TaskId taskId,
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var startedAt = DateTimeOffset.UtcNow;

        if (cancellationToken.IsCancellationRequested)
        {
            return await CancelAsync(
                taskId,
                invocation.ToolId,
                startedAt,
                publishTaskCancelled: false);
        }

        await PublishAsync(TaskEvent.Create(
            taskId,
            TaskEventKind.ToolRequested,
            "Tool requested.",
            TaskExecutionState.Running,
            invocation.ToolId));

        if (!_registry.TryGetTool(invocation.ToolId, out var tool))
        {
            return await FailAsync(
                taskId,
                invocation.ToolId,
                ToolExecutionStatus.ValidationFailed,
                startedAt,
                "Tool was not registered.",
                "tool_not_registered",
                $"Tool '{invocation.ToolId}' is not registered.",
                cancellationToken);
        }

        ToolValidationResult validation;
        try
        {
            validation = await tool.ValidateAsync(invocation.Input, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await CancelAsync(
                taskId,
                tool.Descriptor.Id,
                startedAt,
                publishTaskCancelled: false);
        }
        catch (Exception exception)
        {
            _logger.ToolException(taskId, tool.Descriptor.Id, exception);
            return await FailAsync(
                taskId,
                tool.Descriptor.Id,
                ToolExecutionStatus.UnhandledFailure,
                startedAt,
                "Tool validation failed unexpectedly.",
                "validation_exception",
                "Tool validation failed unexpectedly.",
                cancellationToken);
        }

        if (!validation.IsValid)
        {
            return await FailAsync(
                taskId,
                tool.Descriptor.Id,
                ToolExecutionStatus.ValidationFailed,
                startedAt,
                "Tool input was invalid.",
                validation.SafeErrorCode ?? "validation_failed",
                validation.SafeErrorMessage ?? "Tool input was invalid.",
                cancellationToken);
        }

        PermissionResponse permission;
        try
        {
            permission = await EnsurePermissionAsync(
                taskId,
                tool,
                invocation.Input,
                cancellationToken);
        }
        catch (WorkspaceAccessException exception)
        {
            return await FailAsync(
                taskId,
                tool.Descriptor.Id,
                ToolExecutionStatus.ValidationFailed,
                startedAt,
                exception.SafeErrorMessage,
                exception.SafeErrorCode,
                exception.SafeErrorMessage,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.ToolException(taskId, tool.Descriptor.Id, exception);
            return await FailAsync(
                taskId,
                tool.Descriptor.Id,
                ToolExecutionStatus.UnhandledFailure,
                startedAt,
                "Tool permission requirements failed unexpectedly.",
                "permission_requirements_failed",
                "Tool permission requirements failed unexpectedly.",
                cancellationToken);
        }

        if (permission.Decision == PermissionDecision.CancelTask)
        {
            return await CancelAsync(
                taskId,
                tool.Descriptor.Id,
                startedAt,
                publishTaskCancelled: true);
        }

        if (!permission.IsAllowed)
        {
            return await FailAsync(
                taskId,
                tool.Descriptor.Id,
                ToolExecutionStatus.PermissionDenied,
                startedAt,
                "Tool permission was denied.",
                "permission_denied",
                permission.Summary ?? "The action was not approved.",
                cancellationToken);
        }

        await PublishAsync(TaskEvent.Create(
            taskId,
            TaskEventKind.ToolStarted,
            $"Tool started: {tool.Descriptor.DisplayName}.",
            TaskExecutionState.Running,
            tool.Descriptor.Id));

        using var timeoutCts = new CancellationTokenSource();
        try
        {
            var timeout = tool.Descriptor.RecommendedTimeout ?? _options.DefaultTimeout;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);
            timeoutCts.CancelAfter(timeout);

            var result = await tool.ExecuteAsync(
                    invocation,
                    new TaskExecutionContext(taskId, startedAt),
                    linkedCts.Token)
                .AsTask()
                .WaitAsync(timeoutCts.Token);

            if (result.IsSuccess)
            {
                await PublishAsync(TaskEvent.Create(
                    taskId,
                    TaskEventKind.ToolCompleted,
                    result.Summary,
                    TaskExecutionState.Running,
                    tool.Descriptor.Id));
            }
            else
            {
                await PublishToolFailureAsync(
                    taskId,
                    tool.Descriptor.Id,
                    result.SafeErrorCode ?? "tool_failed",
                    result.SafeErrorMessage ?? result.Summary,
                    CancellationToken.None);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await CancelAsync(
                taskId,
                tool.Descriptor.Id,
                startedAt,
                publishTaskCancelled: false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return await TimeoutAsync(taskId, tool.Descriptor.Id, startedAt);
        }
        catch (TimeoutException)
        {
            return await TimeoutAsync(taskId, tool.Descriptor.Id, startedAt);
        }
        catch (WorkspaceAccessException exception)
        {
            return await FailAsync(
                taskId,
                tool.Descriptor.Id,
                ToolExecutionStatus.ToolFailed,
                startedAt,
                exception.SafeErrorMessage,
                exception.SafeErrorCode,
                exception.SafeErrorMessage,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.ToolException(taskId, tool.Descriptor.Id, exception);
            return await FailAsync(
                taskId,
                tool.Descriptor.Id,
                ToolExecutionStatus.UnhandledFailure,
                startedAt,
                "Tool failed unexpectedly.",
                "tool_exception",
                "Tool failed unexpectedly.",
                cancellationToken);
        }
    }

    private async ValueTask<PermissionResponse> EnsurePermissionAsync(
        TaskId taskId,
        ITypedTool tool,
        object? input,
        CancellationToken cancellationToken)
    {
        var descriptor = tool.Descriptor;
        var requirements = await GetPermissionRequirementsAsync(
            tool,
            input,
            cancellationToken);

        if (!descriptor.RequiresApproval && requirements.Count == 0)
        {
            return new PermissionResponse(
                Guid.Empty,
                PermissionDecision.AllowOnce,
                DateTimeOffset.UtcNow,
                "Tool does not require approval.");
        }

        if (requirements.Count == 0)
        {
            requirements = descriptor.RequiredPermissions
                .Select(permission => new PermissionRequirement(
                    permission,
                    descriptor.PermissionAccessMode,
                    PermissionGrantKey.NormalizeResourceScope(
                        descriptor.PermissionResourceScope),
                    descriptor.PermissionResourceScope ?? descriptor.DisplayName,
                    $"Allow {descriptor.DisplayName} to run?",
                    descriptor.LeavesMachine,
                    descriptor.ChangesState,
                    descriptor.IsReadOnly))
                .ToArray();
        }

        foreach (var requirement in requirements)
        {
            var grantKey = PermissionGrantKey.Create(
                taskId,
                descriptor.Id,
                requirement.Permission,
                requirement.AccessMode,
                requirement.NormalizedResourceScope);

            if (_options.UsePerTaskPermissionCache &&
                grantKey.IsCacheable &&
                _allowedForTask.ContainsKey(grantKey))
            {
                continue;
            }

            var request = new PermissionRequest(
                Guid.NewGuid(),
                taskId,
                descriptor.Id,
                descriptor.DisplayName,
                [requirement.Permission],
                descriptor.RiskLevel,
                requirement.Explanation,
                requirement.NormalizedResourceScope,
                requirement.AccessMode,
                requirement.LeavesMachine,
                requirement.ChangesState,
                requirement.IsReadOnly,
                DateTimeOffset.UtcNow);

            PermissionResponse response;
            await PublishAsync(TaskEvent.Create(
                taskId,
                TaskEventKind.PermissionRequested,
                $"Permission requested for {descriptor.DisplayName}.",
                TaskExecutionState.WaitingForPermission,
                descriptor.Id));

            try
            {
                response = await _permissionBroker.RequestPermissionAsync(
                    request,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return PermissionResponse.CancelTask(
                    request,
                    "Permission request was cancelled.");
            }
            catch (Exception exception)
            {
                response = PermissionResponse.Deny(
                    request,
                    $"Permission broker failed closed: {exception.GetType().Name}.");
            }

            await PublishPermissionDecisionAsync(
                taskId,
                descriptor,
                response);

            if (response.Decision == PermissionDecision.CancelTask)
            {
                return response;
            }

            if (!response.IsAllowed)
            {
                return response;
            }

            if (response.Decision == PermissionDecision.AllowForTask &&
                _options.UsePerTaskPermissionCache &&
                grantKey.IsCacheable)
            {
                _allowedForTask.TryAdd(grantKey, response);
            }
        }

        return new PermissionResponse(
            Guid.Empty,
            PermissionDecision.AllowOnce,
            DateTimeOffset.UtcNow,
            "Permission requirements were approved.");
    }

    private static async ValueTask<IReadOnlyList<PermissionRequirement>>
        GetPermissionRequirementsAsync(
            ITypedTool tool,
            object? input,
            CancellationToken cancellationToken)
    {
        if (tool is not IInvocationPermissionProvider provider)
        {
            return [];
        }

        if (input is null)
        {
            return [];
        }

        return await provider.GetPermissionRequirementsAsync(
            input,
            cancellationToken);
    }

    private ValueTask PublishPermissionDecisionAsync(
        TaskId taskId,
        ToolDescriptor descriptor,
        PermissionResponse response) =>
        PublishAsync(TaskEvent.Create(
            taskId,
            response.IsAllowed
                ? TaskEventKind.PermissionGranted
                : TaskEventKind.PermissionDenied,
            response.IsAllowed
                ? $"Permission granted for {descriptor.DisplayName}."
                : $"Permission denied for {descriptor.DisplayName}.",
            response.IsAllowed
                ? TaskExecutionState.Running
                : TaskExecutionState.Failed,
            descriptor.Id));

    private async ValueTask<ToolResult> CancelAsync(
        TaskId taskId,
        ToolId toolId,
        DateTimeOffset startedAt,
        bool publishTaskCancelled)
    {
        await PublishAsync(TaskEvent.Create(
            taskId,
            TaskEventKind.ToolCancelled,
            "Tool invocation was cancelled.",
            TaskExecutionState.Cancelled,
            toolId));

        if (publishTaskCancelled)
        {
            await PublishAsync(TaskEvent.Create(
                taskId,
                TaskEventKind.TaskCancelled,
                "Task was cancelled by permission decision.",
                TaskExecutionState.Cancelled,
                toolId));
        }

        ClearTaskPermissionCache(taskId);

        return ToolResult.Failure(
            toolId,
            ToolExecutionStatus.Cancelled,
            "Tool invocation was cancelled.",
            DateTimeOffset.UtcNow - startedAt,
            "cancelled",
            "The task was cancelled.");
    }

    private async ValueTask<ToolResult> TimeoutAsync(
        TaskId taskId,
        ToolId toolId,
        DateTimeOffset startedAt)
    {
        await PublishAsync(TaskEvent.Create(
            taskId,
            TaskEventKind.ToolTimedOut,
            "Tool timed out.",
            TaskExecutionState.Failed,
            toolId,
            safeErrorCode: "tool_timeout",
            safeErrorMessage: "The tool did not complete before its timeout."));

        ClearTaskPermissionCache(taskId);

        return ToolResult.Failure(
            toolId,
            ToolExecutionStatus.TimedOut,
            "Tool timed out.",
            DateTimeOffset.UtcNow - startedAt,
            "tool_timeout",
            "The tool did not complete before its timeout.");
    }

    private async ValueTask<ToolResult> FailAsync(
        TaskId taskId,
        ToolId toolId,
        ToolExecutionStatus status,
        DateTimeOffset startedAt,
        string summary,
        string safeErrorCode,
        string safeErrorMessage,
        CancellationToken cancellationToken)
    {
        await PublishToolFailureAsync(
            taskId,
            toolId,
            safeErrorCode,
            safeErrorMessage,
            CancellationToken.None);

        ClearTaskPermissionCache(taskId);

        return ToolResult.Failure(
            toolId,
            status,
            summary,
            DateTimeOffset.UtcNow - startedAt,
            safeErrorCode,
            safeErrorMessage);
    }

    private ValueTask PublishToolFailureAsync(
        TaskId taskId,
        ToolId toolId,
        string safeErrorCode,
        string safeErrorMessage,
        CancellationToken cancellationToken) =>
        PublishAsync(TaskEvent.Create(
            taskId,
            TaskEventKind.ToolFailed,
            "Tool failed.",
            TaskExecutionState.Failed,
            toolId,
            safeErrorCode: safeErrorCode,
            safeErrorMessage: safeErrorMessage));

    private async ValueTask PublishAsync(
        TaskEvent taskEvent,
        CancellationToken cancellationToken = default)
    {
        await _eventBus.PublishAsync(taskEvent, CancellationToken.None);
    }

    private void ClearTaskPermissionCache(TaskId taskId)
    {
        foreach (var key in _allowedForTask.Keys.Where(key => key.TaskId == taskId))
        {
            _allowedForTask.TryRemove(key, out _);
        }
    }
}
