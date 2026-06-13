using System.Collections.Concurrent;
using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;

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

        var permission = await EnsurePermissionAsync(
            taskId,
            tool.Descriptor,
            cancellationToken);

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
        ToolDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (!descriptor.RequiresApproval)
        {
            return new PermissionResponse(
                Guid.Empty,
                PermissionDecision.AllowOnce,
                DateTimeOffset.UtcNow,
                "Tool does not require approval.");
        }

        var grantKeys = descriptor.RequiredPermissions
            .Select(permission => PermissionGrantKey.Create(
                taskId,
                descriptor.Id,
                permission,
                descriptor.PermissionAccessMode,
                descriptor.PermissionResourceScope))
            .ToArray();

        if (_options.UsePerTaskPermissionCache &&
            grantKeys.Length > 0 &&
            grantKeys.All(key => key.IsCacheable) &&
            grantKeys.All(key => _allowedForTask.ContainsKey(key)))
        {
            await PublishAsync(TaskEvent.Create(
                taskId,
                TaskEventKind.PermissionGranted,
                $"Permission already granted for {descriptor.DisplayName}.",
                TaskExecutionState.Running,
                descriptor.Id));
            return new PermissionResponse(
                Guid.Empty,
                PermissionDecision.AllowForTask,
                DateTimeOffset.UtcNow,
                "Permission was already granted for this task and resource.");
        }

        var request = PermissionRequest.Create(
            taskId,
            descriptor,
            $"Allow {descriptor.DisplayName} to run?");

        await PublishAsync(TaskEvent.Create(
            taskId,
            TaskEventKind.PermissionRequested,
            $"Permission requested for {descriptor.DisplayName}.",
            TaskExecutionState.WaitingForPermission,
            descriptor.Id));

        PermissionResponse response;
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

        if (response.Decision == PermissionDecision.AllowForTask &&
            _options.UsePerTaskPermissionCache &&
            grantKeys.All(key => key.IsCacheable))
        {
            foreach (var grantKey in grantKeys)
            {
                _allowedForTask.TryAdd(grantKey, response);
            }
        }

        await PublishAsync(TaskEvent.Create(
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

        return response;
    }

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
