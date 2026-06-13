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
    private readonly ConcurrentDictionary<PermissionCacheKey, PermissionResponse>
        _allowedForTask = [];

    public TypedToolRuntime(
        IToolRegistry registry,
        ITaskEventBus eventBus,
        IPermissionBroker permissionBroker,
        ToolRuntimeOptions? options = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _permissionBroker = permissionBroker ??
            throw new ArgumentNullException(nameof(permissionBroker));
        _options = options ?? ToolRuntimeOptions.Default;

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
                CancellationToken.None);
        }

        await PublishAsync(TaskEvent.Create(
            taskId,
            TaskEventKind.ToolRequested,
            $"Tool requested: {invocation.ToolId}.",
            TaskExecutionState.Running,
            invocation.ToolId), cancellationToken);

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
            return await CancelAsync(taskId, tool.Descriptor.Id, startedAt, cancellationToken);
        }
        catch (Exception exception)
        {
            return await FailAsync(
                taskId,
                tool.Descriptor.Id,
                ToolExecutionStatus.UnhandledFailure,
                startedAt,
                "Tool validation failed unexpectedly.",
                "validation_exception",
                SafeExceptionMessage(exception),
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
            return await CancelAsync(taskId, tool.Descriptor.Id, startedAt, cancellationToken);
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
            tool.Descriptor.Id), cancellationToken);

        try
        {
            var timeout = tool.Descriptor.RecommendedTimeout ?? _options.DefaultTimeout;
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            var result = await tool.ExecuteAsync(
                    invocation,
                    new TaskExecutionContext(taskId, startedAt),
                    linkedCts.Token)
                .AsTask()
                .WaitAsync(timeout, cancellationToken);

            if (result.IsSuccess)
            {
                await PublishAsync(TaskEvent.Create(
                    taskId,
                    TaskEventKind.ToolCompleted,
                    result.Summary,
                    TaskExecutionState.Running,
                    tool.Descriptor.Id), cancellationToken);
            }
            else
            {
                await PublishToolFailureAsync(
                    taskId,
                    tool.Descriptor.Id,
                    result.SafeErrorCode ?? "tool_failed",
                    result.SafeErrorMessage ?? result.Summary,
                    cancellationToken);
            }

            return result;
        }
        catch (TimeoutException)
        {
            return await FailAsync(
                taskId,
                tool.Descriptor.Id,
                ToolExecutionStatus.TimedOut,
                startedAt,
                "Tool timed out.",
                "tool_timeout",
                "The tool did not complete before its timeout.",
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await CancelAsync(taskId, tool.Descriptor.Id, startedAt, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return await FailAsync(
                taskId,
                tool.Descriptor.Id,
                ToolExecutionStatus.TimedOut,
                startedAt,
                "Tool timed out.",
                "tool_timeout",
                "The tool did not complete before its timeout.",
                cancellationToken);
        }
        catch (Exception exception)
        {
            return await FailAsync(
                taskId,
                tool.Descriptor.Id,
                ToolExecutionStatus.UnhandledFailure,
                startedAt,
                "Tool failed unexpectedly.",
                "tool_exception",
                SafeExceptionMessage(exception),
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

        var cacheKey = new PermissionCacheKey(taskId, descriptor.Id);
        if (_options.UsePerTaskPermissionCache &&
            _allowedForTask.TryGetValue(cacheKey, out var cached))
        {
            await PublishAsync(TaskEvent.Create(
                taskId,
                TaskEventKind.PermissionGranted,
                $"Permission already granted for {descriptor.DisplayName}.",
                TaskExecutionState.Running,
                descriptor.Id), cancellationToken);
            return cached;
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
            descriptor.Id), cancellationToken);

        PermissionResponse response;
        try
        {
            response = await _permissionBroker.RequestPermissionAsync(
                request,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            response = PermissionResponse.Deny(
                request,
                $"Permission broker failed closed: {exception.GetType().Name}.");
        }

        if (response.Decision == PermissionDecision.AllowForTask &&
            _options.UsePerTaskPermissionCache)
        {
            _allowedForTask.TryAdd(cacheKey, response);
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
            descriptor.Id), cancellationToken);

        return response;
    }

    private async ValueTask<ToolResult> CancelAsync(
        TaskId taskId,
        ToolId toolId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await PublishAsync(TaskEvent.Create(
            taskId,
            TaskEventKind.TaskCancelled,
            "Tool invocation was cancelled.",
            TaskExecutionState.Cancelled,
            toolId), CancellationToken.None);

        return ToolResult.Failure(
            toolId,
            ToolExecutionStatus.Cancelled,
            "Tool invocation was cancelled.",
            DateTimeOffset.UtcNow - startedAt,
            "cancelled",
            "The task was cancelled.");
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
            cancellationToken);

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
            safeErrorMessage,
            TaskExecutionState.Failed,
            toolId,
            safeErrorCode: safeErrorCode,
            safeErrorMessage: safeErrorMessage), cancellationToken);

    private async ValueTask PublishAsync(
        TaskEvent taskEvent,
        CancellationToken cancellationToken)
    {
        await _eventBus.PublishAsync(taskEvent, cancellationToken);
    }

    private static string SafeExceptionMessage(Exception exception) =>
        $"{exception.GetType().Name}: {exception.Message}";

    private readonly record struct PermissionCacheKey(TaskId TaskId, ToolId ToolId);
}
