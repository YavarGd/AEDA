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
            new ToolInvocation(
                GetCurrentUtcTimeTool.Id,
                new GetCurrentUtcTimeInput()),
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
    public async Task InvokeAsync_ValidationFailurePublishesToolFailed()
    {
        var bus = new TaskEventBus();
        var runtime = CreateRuntime(bus, new FixedPermissionBroker(PermissionDecision.AllowOnce));
        var taskId = TaskId.NewId();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventsTask = CollectAsync(bus.SubscribeAsync(taskId, cancellation.Token), 2);

        var result = await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(GetCurrentUtcTimeTool.Id, "wrong"),
            cancellation.Token);

        var events = await eventsTask;

        Assert.Equal(ToolExecutionStatus.ValidationFailed, result.Status);
        Assert.Equal("input_type_mismatch", result.SafeErrorCode);
        Assert.Contains(events, item => item.Kind == TaskEventKind.ToolFailed);
    }

    [Fact]
    public async Task InvokeAsync_DeniedPermissionDoesNotExecuteTool()
    {
        var bus = new TaskEventBus();
        var tool = new CountingApprovalTool();
        var runtime = CreateRuntime(
            bus,
            new FixedPermissionBroker(PermissionDecision.Deny),
            tool);

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(CountingApprovalTool.ToolId, new EmptyToolInput()));

        Assert.Equal(ToolExecutionStatus.PermissionDenied, result.Status);
        Assert.Equal(0, tool.ExecuteCount);
    }

    [Fact]
    public async Task InvokeAsync_AllowForTaskReusesPermissionForSameTask()
    {
        var broker = new FixedPermissionBroker(PermissionDecision.AllowForTask);
        var tool = new CountingApprovalTool();
        var runtime = CreateRuntime(
            new TaskEventBus(),
            broker,
            tool);
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(CountingApprovalTool.ToolId, new EmptyToolInput()));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(CountingApprovalTool.ToolId, new EmptyToolInput()));

        Assert.Equal(1, broker.RequestCount);
        Assert.Equal(2, tool.ExecuteCount);
    }

    [Fact]
    public async Task InvokeAsync_BrokerFailureDefaultsToPermissionDenied()
    {
        var tool = new CountingApprovalTool();
        var runtime = CreateRuntime(
            new TaskEventBus(),
            new ThrowingPermissionBroker(),
            tool);

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(CountingApprovalTool.ToolId, new EmptyToolInput()));

        Assert.Equal(ToolExecutionStatus.PermissionDenied, result.Status);
        Assert.Equal(0, tool.ExecuteCount);
    }

    [Fact]
    public async Task InvokeAsync_MapsCancellationSeparatelyFromFailure()
    {
        var runtime = CreateRuntime(
            new TaskEventBus(),
            new FixedPermissionBroker(PermissionDecision.AllowOnce),
            new SlowTool(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(SlowTool.ToolId, new EmptyToolInput()),
            cancellation.Token);

        Assert.Equal(ToolExecutionStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task InvokeAsync_MapsTimeout()
    {
        var runtime = CreateRuntime(
            new TaskEventBus(),
            new FixedPermissionBroker(PermissionDecision.AllowOnce),
            new SlowTool(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(25)));

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(SlowTool.ToolId, new EmptyToolInput()));

        Assert.Equal(ToolExecutionStatus.TimedOut, result.Status);
        Assert.Equal("tool_timeout", result.SafeErrorCode);
    }

    [Fact]
    public async Task InvokeAsync_MapsUnhandledToolExceptionsWithoutStackTrace()
    {
        var runtime = CreateRuntime(
            new TaskEventBus(),
            new FixedPermissionBroker(PermissionDecision.AllowOnce),
            new ThrowingTool());

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(ThrowingTool.ToolId, new EmptyToolInput()));

        Assert.Equal(ToolExecutionStatus.UnhandledFailure, result.Status);
        Assert.Equal("tool_exception", result.SafeErrorCode);
        Assert.DoesNotContain(" at ", result.SafeErrorMessage, StringComparison.Ordinal);
    }

    private static TypedToolRuntime CreateRuntime(
        ITaskEventBus eventBus,
        IPermissionBroker permissionBroker,
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
            new ToolRuntimeOptions(TimeSpan.FromSeconds(1), UsePerTaskPermissionCache: true));
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

    private sealed class ThrowingPermissionBroker : IPermissionBroker
    {
        public ValueTask<PermissionResponse> RequestPermissionAsync(
            PermissionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("dialog unavailable");
    }

    private sealed class CountingApprovalTool
        : TypedToolBase<EmptyToolInput, EmptyToolOutput>
    {
        public static ToolId ToolId { get; } = new("test.approval");

        public int ExecuteCount { get; private set; }

        public override ToolDescriptor Descriptor { get; } =
            ToolDescriptor.Create<EmptyToolInput, EmptyToolOutput>(
                ToolId,
                "Approval test",
                "Requires approval.",
                requiredPermissions: [ToolPermission.ReadFile],
                requiresApproval: true);

        public override ValueTask<EmptyToolOutput> ExecuteAsync(
            EmptyToolInput input,
            TaskExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return ValueTask.FromResult(new EmptyToolOutput("ok"));
        }
    }

    private sealed class SlowTool(TimeSpan delay, TimeSpan timeout)
        : TypedToolBase<EmptyToolInput, EmptyToolOutput>
    {
        public static ToolId ToolId { get; } = new("test.slow");

        public override ToolDescriptor Descriptor { get; } =
            ToolDescriptor.Create<EmptyToolInput, EmptyToolOutput>(
                ToolId,
                "Slow test",
                "Runs slowly.",
                recommendedTimeout: timeout);

        public override async ValueTask<EmptyToolOutput> ExecuteAsync(
            EmptyToolInput input,
            TaskExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return new EmptyToolOutput("slow");
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
}
