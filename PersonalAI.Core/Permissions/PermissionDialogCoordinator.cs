namespace PersonalAI.Core.Permissions;

public sealed class PermissionDialogCoordinator : IDisposable
{
    private readonly SemaphoreSlim _dialogLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private bool _disposed;

    public async ValueTask<PermissionResponse> RequestPermissionAsync(
        PermissionRequest request,
        Func<PermissionRequest, PermissionDialogSession, Task<PermissionDialogOutcome>> presentAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(presentAsync);

        if (_disposed)
        {
            return PermissionDialogDecisionMapper.Map(
                request,
                PermissionDialogOutcome.Unavailable,
                "Approval broker is disposed.");
        }

        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);

        try
        {
            await _dialogLock.WaitAsync(waitCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? PermissionDialogDecisionMapper.Map(
                    request,
                    PermissionDialogOutcome.CancelTask,
                    "Permission request was cancelled.")
                : PermissionDialogDecisionMapper.Map(
                    request,
                    PermissionDialogOutcome.Unavailable,
                    "Approval broker is disposed.");
        }

        try
        {
            if (_disposed)
            {
                return PermissionDialogDecisionMapper.Map(
                    request,
                    PermissionDialogOutcome.Unavailable,
                    "Approval broker is disposed.");
            }

            var session = new PermissionDialogSession();
            using var callerRegistration = cancellationToken.Register(() =>
            {
                _ = session.RequestCloseAsync(PermissionDialogOutcome.CancelTask);
            });
            using var disposeRegistration = _disposeCancellation.Token.Register(() =>
            {
                _ = session.RequestCloseAsync(PermissionDialogOutcome.Unavailable);
            });

            PermissionDialogOutcome presentationOutcome;
            try
            {
                presentationOutcome = await presentAsync(request, session);
            }
            catch (Exception exception)
            {
                presentationOutcome = PermissionDialogOutcome.Error;
                return PermissionDialogDecisionMapper.Map(
                    request,
                    presentationOutcome,
                    $"Approval dialog failed closed: {exception.GetType().Name}.");
            }

            return PermissionDialogDecisionMapper.Map(
                request,
                session.OutcomeOr(presentationOutcome));
        }
        finally
        {
            _dialogLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCancellation.Cancel();
    }
}
