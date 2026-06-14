using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Reference;
using PersonalAI.Infrastructure.Tools;

namespace PersonalAI.Tests.Tools;

public sealed class TypedToolRuntimeTests
{
    [Fact]
    public async Task InvokeAsync_RunsReferenceUtcToolAndPublishesLifecycleEvents()
    {
        var bus = new TaskEventBus();
        var runtime = CreateRuntime(bus, new FixedPermissionBroker(PermissionDecision.AllowOnce));
        var taskId = TaskId.NewId();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventsTask = CollectAsync(bus.SubscribeAsync(taskId, cancellation.Token), 3);

        var result = await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(GetCurrentUtcTimeTool.Id, new GetCurrentUtcTimeInput()),
            cancellation.Token);

        var events = await eventsTask;

        Assert.True(result.IsSuccess);
        Assert.IsType<GetCurrentUtcTimeOutput>(result.Output);
        Assert.Collection(
            events,
            first => Assert.Equal(TaskEventKind.ToolRequested, first.Kind),
            second => Assert.Equal(TaskEventKind.ToolStarted, second.Kind),
            third => Assert.Equal(TaskEventKind.ToolCompleted, third.Kind));
    }

    [Fact]
    public async Task InvokeAsync_ApprovalForResourceADoesNotAuthorizeResourceB()
    {
        var broker = new FixedPermissionBroker(PermissionDecision.AllowForTask);
        var runtime = CreateRuntime(
            new TaskEventBus(),
            broker,
            new CountingApprovalTool("A", ToolPermission.ReadFile),
            new CountingApprovalTool("B", ToolPermission.ReadFile));
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(CountingApprovalTool.ToolId("A"), new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(CountingApprovalTool.ToolId("B"), new EmptyToolInput()));

        Assert.Equal(2, broker.RequestCount);
    }

    [Fact]
    public async Task InvokeAsync_SamePermissionAndNormalizedResourceIsReused()
    {
        var broker = new FixedPermissionBroker(PermissionDecision.AllowForTask);
        var tool = new CountingApprovalTool(" C:/Temp/../Project/ ", ToolPermission.ReadFile);
        var runtime = CreateRuntime(new TaskEventBus(), broker, tool);
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(1, broker.RequestCount);
        Assert.Equal(2, tool.ExecuteCount);
    }

    [Fact]
    public async Task InvalidateWorkspacePermissions_RemovesOnlyMatchingWorkspaceScopes()
    {
        var workspaceA = new PersonalAI.Core.Workspaces.WorkspaceId("workspace-a");
        var workspaceB = new PersonalAI.Core.Workspaces.WorkspaceId("workspace-b");
        var toolA = new CountingApprovalTool($"workspace:{workspaceA}:src/a.txt", ToolPermission.ReadWorkspace);
        var toolB = new CountingApprovalTool($"workspace:{workspaceB}:src/b.txt", ToolPermission.ReadWorkspace);
        var broker = new FixedPermissionBroker(PermissionDecision.AllowForTask);
        var runtime = CreateRuntime(new TaskEventBus(), broker, toolA, toolB);
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(toolA.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(toolB.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(toolA.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(toolB.Descriptor.Id, new EmptyToolInput()));

        runtime.InvalidateWorkspacePermissions(workspaceA);

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(toolA.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(toolB.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(3, broker.RequestCount);
    }

    [Fact]
    public async Task InvokeAsync_AllowOnceIsNotReused()
    {
        var broker = new FixedPermissionBroker(PermissionDecision.AllowOnce);
        var tool = new CountingApprovalTool("A", ToolPermission.ReadFile);
        var runtime = CreateRuntime(new TaskEventBus(), broker, tool);
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(2, broker.RequestCount);
    }

    [Fact]
    public async Task InvokeAsync_DifferentPermissionTypesDoNotShareApproval()
    {
        var broker = new FixedPermissionBroker(PermissionDecision.AllowForTask);
        var runtime = CreateRuntime(
            new TaskEventBus(),
            broker,
            new CountingApprovalTool("A", ToolPermission.ReadFile),
            new CountingApprovalTool("A", ToolPermission.WriteFile));
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(CountingApprovalTool.ToolId("A", ToolPermission.ReadFile), new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(CountingApprovalTool.ToolId("A", ToolPermission.WriteFile), new EmptyToolInput()));

        Assert.Equal(2, broker.RequestCount);
    }

    [Fact]
    public async Task InvokeAsync_DifferentTasksDoNotShareApproval()
    {
        var broker = new FixedPermissionBroker(PermissionDecision.AllowForTask);
        var tool = new CountingApprovalTool("A", ToolPermission.ReadFile);
        var runtime = CreateRuntime(new TaskEventBus(), broker, tool);

        await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(2, broker.RequestCount);
    }

    [Fact]
    public async Task InvokeAsync_EmptyScopesDoNotCreateBroadApproval()
    {
        var broker = new FixedPermissionBroker(PermissionDecision.AllowForTask);
        var tool = new CountingApprovalTool(string.Empty, ToolPermission.ReadFile);
        var runtime = CreateRuntime(new TaskEventBus(), broker, tool);
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(2, broker.RequestCount);
    }

    [Fact]
    public async Task InvokeAsync_DeniedPermissionDoesNotExecuteTool()
    {
        var tool = new CountingApprovalTool("A", ToolPermission.ReadFile);
        var runtime = CreateRuntime(
            new TaskEventBus(),
            new FixedPermissionBroker(PermissionDecision.Deny),
            tool);

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(ToolExecutionStatus.PermissionDenied, result.Status);
        Assert.Equal(0, tool.ExecuteCount);
    }

    [Fact]
    public async Task InvokeAsync_PermissionDecisionCancelTaskDoesNotExecuteTool()
    {
        var bus = new TaskEventBus();
        var tool = new CountingApprovalTool("A", ToolPermission.ReadFile);
        var runtime = CreateRuntime(
            bus,
            new FixedPermissionBroker(PermissionDecision.CancelTask),
            tool);
        var taskId = TaskId.NewId();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventsTask = CollectAsync(bus.SubscribeAsync(taskId, cancellation.Token), 5);

        var result = await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        var events = await eventsTask;

        Assert.Equal(ToolExecutionStatus.Cancelled, result.Status);
        Assert.Equal(0, tool.ExecuteCount);
        Assert.Contains(events, item => item.Kind == TaskEventKind.TaskCancelled);
    }

    [Fact]
    public async Task InvokeAsync_CallerCancellationClearsCachedAllowForTaskPermission()
    {
        var broker = new QueuePermissionBroker(
            PermissionDecision.AllowForTask,
            PermissionDecision.AllowOnce);
        var tool = new CountingApprovalTool("A", ToolPermission.ReadFile);
        var controlledTool = new ControlledApprovalTool("A", TimeSpan.FromSeconds(10));
        var runtime = CreateRuntime(
            new TaskEventBus(),
            broker,
            tool,
            controlledTool);
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        using var cancellation = new CancellationTokenSource();
        var invocation = runtime.InvokeAsync(
            taskId,
            new ToolInvocation(controlledTool.Descriptor.Id, new EmptyToolInput()),
            cancellation.Token);
        await controlledTool.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var cancelled = await invocation;

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(ToolExecutionStatus.Cancelled, cancelled.Status);
        Assert.Equal(3, broker.RequestCount);
    }

    [Fact]
    public async Task InvokeAsync_PermissionDecisionCancelTaskClearsCachedAllowForTaskPermission()
    {
        var broker = new QueuePermissionBroker(
            PermissionDecision.AllowForTask,
            PermissionDecision.CancelTask,
            PermissionDecision.AllowOnce);
        var tool = new CountingApprovalTool("A", ToolPermission.ReadFile);
        var cancelTool = new CountingApprovalTool("A", ToolPermission.ReadFile, "cancel");
        var runtime = CreateRuntime(
            new TaskEventBus(),
            broker,
            tool,
            cancelTool);
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));
        var cancelled = await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(cancelTool.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(ToolExecutionStatus.Cancelled, cancelled.Status);
        Assert.Equal(3, broker.RequestCount);
    }

    [Fact]
    public async Task InvokeAsync_BrokerFailureDefaultsToPermissionDenied()
    {
        var tool = new CountingApprovalTool("A", ToolPermission.ReadFile);
        var runtime = CreateRuntime(
            new TaskEventBus(),
            new ThrowingPermissionBroker(),
            tool);

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(ToolExecutionStatus.PermissionDenied, result.Status);
        Assert.Equal(0, tool.ExecuteCount);
    }

    [Fact]
    public async Task InvokeAsync_PermissionBrokerCancellationReturnsCancelled()
    {
        var tool = new CountingApprovalTool("A", ToolPermission.ReadFile);
        var runtime = CreateRuntime(
            new TaskEventBus(),
            new CancellingPermissionBroker(),
            tool);

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(ToolExecutionStatus.Cancelled, result.Status);
        Assert.Equal(0, tool.ExecuteCount);
    }

    [Fact]
    public async Task InvokeAsync_CallerCancellationBeforeExecutionPublishesToolCancelled()
    {
        var bus = new TaskEventBus();
        var runtime = CreateRuntime(
            bus,
            new FixedPermissionBroker(PermissionDecision.AllowOnce),
            new ControlledTool(TimeSpan.FromSeconds(10)));
        var taskId = TaskId.NewId();
        using var eventsCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventsTask = CollectAsync(bus.SubscribeAsync(taskId, eventsCancellation.Token), 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(ControlledTool.ToolId, new EmptyToolInput()),
            cancellation.Token);
        var events = await eventsTask;

        Assert.Equal(ToolExecutionStatus.Cancelled, result.Status);
        Assert.Equal(TaskEventKind.ToolCancelled, Assert.Single(events).Kind);
    }

    [Fact]
    public async Task InvokeAsync_CancellationDuringExecutionIsNotTimeout()
    {
        var tool = new ControlledTool(TimeSpan.FromSeconds(10));
        var runtime = CreateRuntime(
            new TaskEventBus(),
            new FixedPermissionBroker(PermissionDecision.AllowOnce),
            tool);
        using var cancellation = new CancellationTokenSource();

        var invocation = runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(ControlledTool.ToolId, new EmptyToolInput()),
            cancellation.Token);
        await tool.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        var result = await invocation;

        Assert.Equal(ToolExecutionStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task InvokeAsync_TimeoutDuringExecutionIsNotCancellation()
    {
        var bus = new TaskEventBus();
        var tool = new ControlledTool(TimeSpan.FromMilliseconds(25));
        var runtime = CreateRuntime(
            bus,
            new FixedPermissionBroker(PermissionDecision.AllowOnce),
            tool);
        var taskId = TaskId.NewId();
        using var eventsCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventsTask = CollectAsync(bus.SubscribeAsync(taskId, eventsCancellation.Token), 3);

        var result = await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(ControlledTool.ToolId, new EmptyToolInput()));
        var events = await eventsTask;

        Assert.Equal(ToolExecutionStatus.TimedOut, result.Status);
        Assert.Contains(events, item => item.Kind == TaskEventKind.ToolTimedOut);
        Assert.DoesNotContain(events, item => item.Kind == TaskEventKind.ToolCancelled);
    }

    [Fact]
    public async Task InvokeAsync_UnhandledToolExceptionLogsTechnicalErrorButPublishesSafeEvent()
    {
        var bus = new TaskEventBus();
        var logger = new CapturingToolRuntimeLogger();
        var runtime = CreateRuntime(
            bus,
            new FixedPermissionBroker(PermissionDecision.AllowOnce),
            logger,
            new ThrowingTool());
        var taskId = TaskId.NewId();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventsTask = CollectAsync(bus.SubscribeAsync(taskId, cancellation.Token), 3);

        var result = await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(ThrowingTool.ToolId, new EmptyToolInput()));
        var events = await eventsTask;

        Assert.Equal(ToolExecutionStatus.UnhandledFailure, result.Status);
        Assert.Equal("tool_exception", result.SafeErrorCode);
        Assert.Equal("Tool failed unexpectedly.", result.SafeErrorMessage);
        Assert.Single(logger.Exceptions);
        Assert.DoesNotContain(events, item => item.Summary.Contains("boom", StringComparison.Ordinal));
        Assert.DoesNotContain(events, item => item.SafeErrorMessage?.Contains(" at ", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotAddToolInputToEvents()
    {
        var bus = new TaskEventBus();
        var runtime = CreateRuntime(
            bus,
            new FixedPermissionBroker(PermissionDecision.AllowOnce),
            new ControlledTool(TimeSpan.FromSeconds(1)));
        var taskId = TaskId.NewId();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventsTask = CollectAsync(bus.SubscribeAsync(taskId, cancellation.Token), 3);

        _ = await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(ControlledTool.ToolId, new SecretInput("api_key=supersecret")));
        var events = await eventsTask;

        Assert.DoesNotContain(
            events,
            item => item.Summary.Contains("supersecret", StringComparison.Ordinal) ||
                item.SafeMetadata?.Values.Any(value =>
                    value.Contains("supersecret", StringComparison.Ordinal)) == true);
    }

    [Fact]
    public async Task InvokeAsync_AllowOnceThenAllowForTask_CachesOnlySecondRequirement()
    {
        var broker = new QueuePermissionBroker(
            PermissionDecision.AllowOnce,
            PermissionDecision.AllowForTask,
            PermissionDecision.AllowOnce);
        var tool = new MultiRequirementTool("mixed.once.then.task");
        var runtime = CreateRuntime(new TaskEventBus(), broker, tool);
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(3, broker.RequestCount);
        Assert.Equal(["SCOPE-A", "SCOPE-B", "SCOPE-A"], broker.Requests.Select(request => request.ResourceScope));
        Assert.Equal(2, tool.ExecuteCount);
    }

    [Fact]
    public async Task InvokeAsync_AllowForTaskThenAllowOnce_CachesOnlyFirstRequirement()
    {
        var broker = new QueuePermissionBroker(
            PermissionDecision.AllowForTask,
            PermissionDecision.AllowOnce,
            PermissionDecision.AllowOnce);
        var tool = new MultiRequirementTool("mixed.task.then.once");
        var runtime = CreateRuntime(new TaskEventBus(), broker, tool);
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(3, broker.RequestCount);
        Assert.Equal(["SCOPE-A", "SCOPE-B", "SCOPE-B"], broker.Requests.Select(request => request.ResourceScope));
        Assert.Equal(2, tool.ExecuteCount);
    }

    [Fact]
    public async Task InvokeAsync_BothAllowForTask_CachesBothRequirements()
    {
        var broker = new QueuePermissionBroker(
            PermissionDecision.AllowForTask,
            PermissionDecision.AllowForTask);
        var tool = new MultiRequirementTool("both.task");
        var runtime = CreateRuntime(new TaskEventBus(), broker, tool);
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(2, broker.RequestCount);
        Assert.Equal(2, tool.ExecuteCount);
    }

    [Fact]
    public async Task InvokeAsync_BothAllowOnce_CachesNeitherRequirement()
    {
        var broker = new QueuePermissionBroker(
            PermissionDecision.AllowOnce,
            PermissionDecision.AllowOnce,
            PermissionDecision.AllowOnce,
            PermissionDecision.AllowOnce);
        var tool = new MultiRequirementTool("both.once");
        var runtime = CreateRuntime(new TaskEventBus(), broker, tool);
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(4, broker.RequestCount);
        Assert.Equal(2, tool.ExecuteCount);
    }

    [Fact]
    public async Task InvokeAsync_DenialOnSecondRequirementStopsAndDoesNotExecute()
    {
        var broker = new QueuePermissionBroker(
            PermissionDecision.AllowForTask,
            PermissionDecision.Deny);
        var tool = new MultiRequirementTool("deny.second");
        var runtime = CreateRuntime(new TaskEventBus(), broker, tool);

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(ToolExecutionStatus.PermissionDenied, result.Status);
        Assert.Equal(2, broker.RequestCount);
        Assert.Equal(0, tool.ExecuteCount);
    }

    [Fact]
    public async Task InvokeAsync_CancelTaskOnSecondRequirementStopsAndDoesNotExecute()
    {
        var broker = new QueuePermissionBroker(
            PermissionDecision.AllowForTask,
            PermissionDecision.CancelTask);
        var tool = new MultiRequirementTool("cancel.second");
        var runtime = CreateRuntime(new TaskEventBus(), broker, tool);

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(ToolExecutionStatus.Cancelled, result.Status);
        Assert.Equal(2, broker.RequestCount);
        Assert.Equal(0, tool.ExecuteCount);
    }

    [Fact]
    public async Task InvokeAsync_CachedFirstRequirementSkipsDialogButUncachedSecondPrompts()
    {
        var broker = new QueuePermissionBroker(
            PermissionDecision.AllowForTask,
            PermissionDecision.AllowForTask,
            PermissionDecision.AllowOnce);
        var tool = new DynamicSecondRequirementTool();
        var runtime = CreateRuntime(new TaskEventBus(), broker, tool);
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new ScopedInput("scope-b")));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new ScopedInput("scope-c")));

        Assert.Equal(3, broker.RequestCount);
        Assert.Equal(["SCOPE-A", "SCOPE-B", "SCOPE-C"], broker.Requests.Select(request => request.ResourceScope));
    }

    [Fact]
    public async Task InvokeAsync_DifferentResourcesRemainIsolatedForMultiRequirementTool()
    {
        var broker = new QueuePermissionBroker(
            PermissionDecision.AllowForTask,
            PermissionDecision.AllowForTask,
            PermissionDecision.AllowForTask,
            PermissionDecision.AllowForTask);
        var first = new MultiRequirementTool("isolated.one", firstScope: "scope-a", secondScope: "scope-b");
        var second = new MultiRequirementTool("isolated.two", firstScope: "scope-a2", secondScope: "scope-b2");
        var runtime = CreateRuntime(new TaskEventBus(), broker, first, second);
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(taskId, new ToolInvocation(first.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(taskId, new ToolInvocation(second.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(4, broker.RequestCount);
    }

    [Fact]
    public async Task InvokeAsync_EmptyRequirementScopesRemainNonCacheable()
    {
        var broker = new QueuePermissionBroker(
            PermissionDecision.AllowForTask,
            PermissionDecision.AllowForTask,
            PermissionDecision.AllowForTask,
            PermissionDecision.AllowForTask);
        var tool = new MultiRequirementTool("empty.scopes", firstScope: string.Empty, secondScope: string.Empty);
        var runtime = CreateRuntime(new TaskEventBus(), broker, tool);
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(taskId, new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));
        await runtime.InvokeAsync(taskId, new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.Equal(4, broker.RequestCount);
    }

    [Fact]
    public async Task InvokeAsync_ExecutesOnlyAfterAllRequirementsApproved()
    {
        var broker = new QueuePermissionBroker(
            PermissionDecision.AllowOnce,
            PermissionDecision.AllowOnce);
        var tool = new MultiRequirementTool("all.approved");
        var runtime = CreateRuntime(new TaskEventBus(), broker, tool);

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, broker.RequestCount);
        Assert.Equal(1, tool.ExecuteCount);
    }

    [Fact]
    public async Task InvokeAsync_InvocationPermissionProviderExceptionIsSafeAndLogged()
    {
        var bus = new TaskEventBus();
        var logger = new CapturingToolRuntimeLogger();
        var tool = new ThrowingPermissionProviderTool();
        var runtime = CreateRuntime(
            bus,
            new FixedPermissionBroker(PermissionDecision.AllowOnce),
            logger,
            tool);
        var taskId = TaskId.NewId();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventsTask = CollectAsync(bus.SubscribeAsync(taskId, cancellation.Token), 2);

        var result = await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(tool.Descriptor.Id, new EmptyToolInput()));
        var events = await eventsTask;

        Assert.Equal(ToolExecutionStatus.UnhandledFailure, result.Status);
        Assert.Equal("permission_requirements_failed", result.SafeErrorCode);
        Assert.Equal("Tool permission requirements failed unexpectedly.", result.SafeErrorMessage);
        Assert.Single(logger.Exceptions);
        Assert.DoesNotContain(events, item => item.Summary.Contains("sensitive", StringComparison.Ordinal));
        Assert.DoesNotContain(events, item => item.SafeErrorMessage?.Contains("sensitive", StringComparison.Ordinal) == true);
    }

    private static TypedToolRuntime CreateRuntime(
        ITaskEventBus eventBus,
        IPermissionBroker permissionBroker,
        params ITypedTool[] additionalTools) =>
        CreateRuntime(eventBus, permissionBroker, NullToolRuntimeLogger.Instance, additionalTools);

    private static TypedToolRuntime CreateRuntime(
        ITaskEventBus eventBus,
        IPermissionBroker permissionBroker,
        IToolRuntimeLogger logger,
        params ITypedTool[] additionalTools)
    {
        var registry = new TypedToolRegistry();
        registry.Register(new GetCurrentUtcTimeTool());

        foreach (var tool in additionalTools)
        {
            registry.Register(tool);
        }

        return new TypedToolRuntime(
            registry,
            eventBus,
            permissionBroker,
            new ToolRuntimeOptions(TimeSpan.FromSeconds(1), UsePerTaskPermissionCache: true),
            logger);
    }

    private static async Task<IReadOnlyList<TaskEvent>> CollectAsync(
        IAsyncEnumerable<TaskEvent> events,
        int count)
    {
        var received = new List<TaskEvent>();

        await foreach (var taskEvent in events)
        {
            received.Add(taskEvent);

            if (received.Count == count)
            {
                return received;
            }
        }

        return received;
    }

    private sealed record EmptyToolInput;

    private sealed record SecretInput(string Secret);

    private sealed record ScopedInput(string SecondScope);

    private sealed record EmptyToolOutput(string Value);

    private sealed class FixedPermissionBroker(PermissionDecision decision)
        : IPermissionBroker
    {
        public int RequestCount { get; private set; }

        public ValueTask<PermissionResponse> RequestPermissionAsync(
            PermissionRequest request,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            var response = decision switch
            {
                PermissionDecision.AllowOnce => PermissionResponse.AllowOnce(request),
                PermissionDecision.AllowForTask => PermissionResponse.AllowForTask(request),
                PermissionDecision.CancelTask => PermissionResponse.CancelTask(request),
                _ => PermissionResponse.Deny(request)
            };
            return ValueTask.FromResult(response);
        }
    }

    private sealed class QueuePermissionBroker(params PermissionDecision[] decisions)
        : IPermissionBroker
    {
        private readonly Queue<PermissionDecision> _decisions = new(decisions);

        public int RequestCount { get; private set; }

        public List<PermissionRequest> Requests { get; } = [];

        public ValueTask<PermissionResponse> RequestPermissionAsync(
            PermissionRequest request,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            Requests.Add(request);
            var decision = _decisions.Count > 0
                ? _decisions.Dequeue()
                : PermissionDecision.AllowOnce;
            var response = decision switch
            {
                PermissionDecision.AllowForTask => PermissionResponse.AllowForTask(request),
                PermissionDecision.CancelTask => PermissionResponse.CancelTask(request),
                PermissionDecision.Deny => PermissionResponse.Deny(request),
                _ => PermissionResponse.AllowOnce(request)
            };
            return ValueTask.FromResult(response);
        }
    }

    private sealed class ThrowingPermissionBroker : IPermissionBroker
    {
        public ValueTask<PermissionResponse> RequestPermissionAsync(
            PermissionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("dialog unavailable");
    }

    private sealed class CancellingPermissionBroker : IPermissionBroker
    {
        public ValueTask<PermissionResponse> RequestPermissionAsync(
            PermissionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException();
    }

    private sealed class CountingApprovalTool
        : TypedToolBase<EmptyToolInput, EmptyToolOutput>
    {
        private readonly ToolPermission _permission;
        private readonly string _resourceScope;

        public CountingApprovalTool(
            string resourceScope,
            ToolPermission permission,
            string suffix = "")
        {
            _resourceScope = resourceScope;
            _permission = permission;
            Descriptor = ToolDescriptor.Create<EmptyToolInput, EmptyToolOutput>(
                ToolId(resourceScope, permission, suffix),
                "Approval test",
                "Requires approval.",
                requiredPermissions: [permission],
                requiresApproval: true,
                permissionResourceScope: resourceScope,
                permissionAccessMode: permission == ToolPermission.WriteFile
                    ? PermissionAccessMode.Write
                    : PermissionAccessMode.Read);
        }

        public static ToolId ToolId(string resourceScope) =>
            ToolId(resourceScope, ToolPermission.ReadFile);

        public static ToolId ToolId(
            string resourceScope,
            ToolPermission permission,
            string suffix = "") =>
            new($"test.approval.{permission}.{PermissionGrantKey.NormalizeResourceScope(resourceScope)}.{suffix}");

        public int ExecuteCount { get; private set; }

        public override ToolDescriptor Descriptor { get; }

        public override ValueTask<EmptyToolOutput> ExecuteAsync(
            EmptyToolInput input,
            TaskExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            _ = _permission;
            _ = _resourceScope;
            ExecuteCount++;
            return ValueTask.FromResult(new EmptyToolOutput("ok"));
        }
    }

    private sealed class ControlledApprovalTool(string resourceScope, TimeSpan timeout)
        : TypedToolBase<EmptyToolInput, EmptyToolOutput>
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ToolDescriptor Descriptor { get; } =
            ToolDescriptor.Create<EmptyToolInput, EmptyToolOutput>(
                new($"test.controlled.approval.{PermissionGrantKey.NormalizeResourceScope(resourceScope)}"),
                "Controlled approval test",
                "Requires approval and waits until cancelled.",
                requiredPermissions: [ToolPermission.ReadFile],
                requiresApproval: true,
                recommendedTimeout: timeout,
                permissionResourceScope: resourceScope,
                permissionAccessMode: PermissionAccessMode.Read);

        public override async ValueTask<EmptyToolOutput> ExecuteAsync(
            EmptyToolInput input,
            TaskExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new EmptyToolOutput("done");
        }
    }

    private sealed class MultiRequirementTool(
        string idSuffix,
        string firstScope = "scope-a",
        string secondScope = "scope-b")
        : TypedToolBase<EmptyToolInput, EmptyToolOutput>, IInvocationPermissionProvider
    {
        public int ExecuteCount { get; private set; }

        public override ToolDescriptor Descriptor { get; } =
            ToolDescriptor.Create<EmptyToolInput, EmptyToolOutput>(
                new ToolId($"test.multi-permission.{idSuffix}"),
                "Multi permission test",
                "Requires two independent permissions.",
                requiresApproval: true);

        public ValueTask<IReadOnlyList<PermissionRequirement>> GetPermissionRequirementsAsync(
            object input,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<PermissionRequirement>>(
                [CreateRequirement(ToolPermission.ReadFile, firstScope),
                    CreateRequirement(ToolPermission.ReadWorkspace, secondScope)]);

        public override ValueTask<EmptyToolOutput> ExecuteAsync(
            EmptyToolInput input,
            TaskExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return ValueTask.FromResult(new EmptyToolOutput("ok"));
        }
    }

    private sealed class DynamicSecondRequirementTool
        : TypedToolBase<ScopedInput, EmptyToolOutput>, IInvocationPermissionProvider
    {
        public override ToolDescriptor Descriptor { get; } =
            ToolDescriptor.Create<ScopedInput, EmptyToolOutput>(
                new ToolId("test.multi-permission.dynamic"),
                "Dynamic permission test",
                "Requires one cached and one dynamic permission.",
                requiresApproval: true);

        public ValueTask<IReadOnlyList<PermissionRequirement>> GetPermissionRequirementsAsync(
            object input,
            CancellationToken cancellationToken = default)
        {
            var typed = (ScopedInput)input;
            return ValueTask.FromResult<IReadOnlyList<PermissionRequirement>>(
                [CreateRequirement(ToolPermission.ReadFile, "scope-a"),
                    CreateRequirement(ToolPermission.ReadWorkspace, typed.SecondScope)]);
        }

        public override ValueTask<EmptyToolOutput> ExecuteAsync(
            ScopedInput input,
            TaskExecutionContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EmptyToolOutput("ok"));
    }

    private sealed class ThrowingPermissionProviderTool
        : TypedToolBase<EmptyToolInput, EmptyToolOutput>, IInvocationPermissionProvider
    {
        public override ToolDescriptor Descriptor { get; } =
            ToolDescriptor.Create<EmptyToolInput, EmptyToolOutput>(
                new ToolId("test.permission-provider.throwing"),
                "Throwing permission provider",
                "Throws while building permission requirements.",
                requiresApproval: true);

        public ValueTask<IReadOnlyList<PermissionRequirement>> GetPermissionRequirementsAsync(
            object input,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("sensitive provider failure");

        public override ValueTask<EmptyToolOutput> ExecuteAsync(
            EmptyToolInput input,
            TaskExecutionContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EmptyToolOutput("should not run"));
    }

    private static PermissionRequirement CreateRequirement(
        ToolPermission permission,
        string scope) =>
        new(
            permission,
            PermissionAccessMode.Read,
            PermissionGrantKey.NormalizeResourceScope(scope),
            scope,
            $"Read {scope}.",
            IsReadOnly: true);

    private sealed class ControlledTool(TimeSpan timeout)
        : TypedToolBase<object, EmptyToolOutput>
    {
        public static ToolId ToolId { get; } = new("test.controlled");

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ToolDescriptor Descriptor { get; } =
            ToolDescriptor.Create<object, EmptyToolOutput>(
                ToolId,
                "Controlled test",
                "Waits until cancelled.",
                recommendedTimeout: timeout);

        public override async ValueTask<EmptyToolOutput> ExecuteAsync(
            object input,
            TaskExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new EmptyToolOutput("done");
        }
    }

    private sealed class ThrowingTool
        : TypedToolBase<EmptyToolInput, EmptyToolOutput>
    {
        public static ToolId ToolId { get; } = new("test.throwing");

        public override ToolDescriptor Descriptor { get; } =
            ToolDescriptor.Create<EmptyToolInput, EmptyToolOutput>(
                ToolId,
                "Throwing test",
                "Throws during execution.");

        public override ValueTask<EmptyToolOutput> ExecuteAsync(
            EmptyToolInput input,
            TaskExecutionContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class CapturingToolRuntimeLogger : IToolRuntimeLogger
    {
        public List<Exception> Exceptions { get; } = [];

        public void ToolException(TaskId taskId, ToolId toolId, Exception exception)
        {
            Exceptions.Add(exception);
        }
    }
}
