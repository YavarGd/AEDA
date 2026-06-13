using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools.Reference;

namespace PersonalAI.Tests.Permissions;

public sealed class PermissionDialogCoordinatorTests
{
    [Fact]
    public async Task CancellationWhileWaitingForSemaphore_ReturnsCancelTask()
    {
        using var coordinator = new PermissionDialogCoordinator();
        var active = new ControlledPresentation();
        var first = coordinator.RequestPermissionAsync(
            CreateRequest(),
            active.PresentAsync);
        await active.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var cancellation = new CancellationTokenSource();
        var second = coordinator.RequestPermissionAsync(
            CreateRequest(),
            new ControlledPresentation().PresentAsync,
            cancellation.Token);
        cancellation.Cancel();

        var secondResponse = await second;
        await active.CompleteAsync(PermissionDialogOutcome.Deny);
        _ = await first;

        Assert.Equal(PermissionDecision.CancelTask, secondResponse.Decision);
    }

    [Fact]
    public async Task CancellationWhileDialogIsActive_ClosesBeforeReturning()
    {
        using var coordinator = new PermissionDialogCoordinator();
        var presentation = new ControlledPresentation();
        using var cancellation = new CancellationTokenSource();
        var request = coordinator.RequestPermissionAsync(
            CreateRequest(),
            presentation.PresentAsync,
            cancellation.Token);
        await presentation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await presentation.CloseRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(request.AsTask().IsCompleted);

        presentation.ReleaseClose();
        var response = await request;

        Assert.Equal(PermissionDecision.CancelTask, response.Decision);
        Assert.True(presentation.Completed);
    }

    [Fact]
    public async Task ActiveDialogIsClosedBeforeNextRequestProceeds()
    {
        using var coordinator = new PermissionDialogCoordinator();
        var firstPresentation = new ControlledPresentation();
        using var cancellation = new CancellationTokenSource();
        var first = coordinator.RequestPermissionAsync(
            CreateRequest(),
            firstPresentation.PresentAsync,
            cancellation.Token);
        await firstPresentation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await firstPresentation.CloseRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondPresentation = new ControlledPresentation();
        var second = coordinator.RequestPermissionAsync(
            CreateRequest(),
            secondPresentation.PresentAsync);
        await Task.Delay(50);

        Assert.False(secondPresentation.Started.Task.IsCompleted);

        firstPresentation.ReleaseClose();
        _ = await first;
        await secondPresentation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await secondPresentation.CompleteAsync(PermissionDialogOutcome.Deny);
        _ = await second;
    }

    [Fact]
    public async Task DisposalWhileWaiting_ReturnsFailClosedDenial()
    {
        var coordinator = new PermissionDialogCoordinator();
        var active = new ControlledPresentation();
        var first = coordinator.RequestPermissionAsync(
            CreateRequest(),
            active.PresentAsync);
        await active.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = coordinator.RequestPermissionAsync(
            CreateRequest(),
            new ControlledPresentation().PresentAsync);
        coordinator.Dispose();

        var secondResponse = await second;
        active.ReleaseClose();
        _ = await first;

        Assert.Equal(PermissionDecision.Deny, secondResponse.Decision);
    }

    [Fact]
    public async Task DisposalWhileActive_ClosesAndReturnsFailClosedDenial()
    {
        var coordinator = new PermissionDialogCoordinator();
        var presentation = new ControlledPresentation();
        var request = coordinator.RequestPermissionAsync(
            CreateRequest(),
            presentation.PresentAsync);
        await presentation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        coordinator.Dispose();
        await presentation.CloseRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        presentation.ReleaseClose();
        var response = await request;

        Assert.Equal(PermissionDecision.Deny, response.Decision);
        Assert.True(presentation.Completed);
    }

    [Fact]
    public async Task ExplicitApprovalCannotWinAfterCancellation()
    {
        using var coordinator = new PermissionDialogCoordinator();
        var presentation = new ControlledPresentation(
            tryApproveAfterClose: true);
        using var cancellation = new CancellationTokenSource();
        var request = coordinator.RequestPermissionAsync(
            CreateRequest(),
            presentation.PresentAsync,
            cancellation.Token);
        await presentation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await presentation.CloseRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        presentation.ReleaseClose();
        var response = await request;

        Assert.Equal(PermissionDecision.CancelTask, response.Decision);
    }

    [Fact]
    public async Task DialogPresentationException_Denies()
    {
        using var coordinator = new PermissionDialogCoordinator();

        var response = await coordinator.RequestPermissionAsync(
            CreateRequest(),
            (_, _) => throw new InvalidOperationException("dialog failed"));

        Assert.Equal(PermissionDecision.Deny, response.Decision);
    }

    [Fact]
    public async Task PresentationsAreSerializedOneAtATime()
    {
        using var coordinator = new PermissionDialogCoordinator();
        var activeCount = 0;
        var maxActiveCount = 0;

        var requests = Enumerable.Range(0, 20)
            .Select(_ => coordinator.RequestPermissionAsync(
                CreateRequest(),
                async (_, _) =>
                {
                    var active = Interlocked.Increment(ref activeCount);
                    maxActiveCount = Math.Max(maxActiveCount, active);
                    await Task.Delay(5);
                    Interlocked.Decrement(ref activeCount);
                    return PermissionDialogOutcome.Deny;
                }).AsTask())
            .ToArray();

        await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, maxActiveCount);
    }

    private static PermissionRequest CreateRequest() =>
        new(
            Guid.NewGuid(),
            TaskId.NewId(),
            GetCurrentUtcTimeTool.Id,
            "Tool",
            [ToolPermission.ReadSystemTime],
            PermissionRiskLevel.Low,
            "Approve?",
            "clock",
            PermissionAccessMode.Read);

    private sealed class ControlledPresentation(bool tryApproveAfterClose = false)
    {
        private readonly TaskCompletionSource _finish =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _closeRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private PermissionDialogOutcome _outcome = PermissionDialogOutcome.Dismissed;
        private PermissionDialogSession? _session;

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CloseRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Completed { get; private set; }

        public async Task<PermissionDialogOutcome> PresentAsync(
            PermissionRequest request,
            PermissionDialogSession session)
        {
            _session = session;
            await session.RegisterCloseAsync(async () =>
            {
                CloseRequested.TrySetResult();
                await _closeRelease.Task;

                if (tryApproveAfterClose)
                {
                    session.TrySetOutcome(PermissionDialogOutcome.AllowOnce);
                }

                _finish.TrySetResult();
            });

            Started.TrySetResult();
            await _finish.Task;
            Completed = true;
            return _outcome;
        }

        public async Task CompleteAsync(PermissionDialogOutcome outcome)
        {
            _outcome = outcome;
            _session?.TrySetOutcome(outcome);
            _finish.TrySetResult();
            await Task.Yield();
        }

        public void ReleaseClose()
        {
            _closeRelease.TrySetResult();
        }
    }
}
