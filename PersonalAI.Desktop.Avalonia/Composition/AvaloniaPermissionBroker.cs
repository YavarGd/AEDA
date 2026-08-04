using Avalonia.Controls;
using Avalonia.Threading;
using PersonalAI.Core.Permissions;
using PersonalAI.Desktop.Avalonia.Views.Dialogs;

namespace PersonalAI.Desktop.Avalonia.Composition;

public sealed class AvaloniaPermissionBroker(Func<Window?> getOwner) :
    IPermissionBroker,
    IDisposable
{
    private readonly PermissionDialogCoordinator _coordinator = new();

    public ValueTask<PermissionResponse> RequestPermissionAsync(
        PermissionRequest request,
        CancellationToken cancellationToken = default) =>
        _coordinator.RequestPermissionAsync(
            request,
            PresentOnUiThreadAsync,
            cancellationToken);

    public void Dispose() => _coordinator.Dispose();

    private async Task<PermissionDialogOutcome> PresentOnUiThreadAsync(
        PermissionRequest request,
        PermissionDialogSession session)
    {
        Window? owner;
        try
        {
            owner = getOwner();
        }
        catch
        {
            return PermissionDialogOutcome.Error;
        }

        if (owner is null)
        {
            return PermissionDialogOutcome.Unavailable;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            return await ShowDialogAsync(owner, request, session);
        }

        var completion = new TaskCompletionSource<PermissionDialogOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await ShowDialogAsync(owner, request, session));
            }
            catch
            {
                completion.TrySetResult(PermissionDialogOutcome.Error);
            }
        });
        return await completion.Task;
    }

    private async Task<PermissionDialogOutcome> ShowDialogAsync(
        Window owner,
        PermissionRequest request,
        PermissionDialogSession session)
    {
        if (!owner.IsVisible)
        {
            return PermissionDialogOutcome.Unavailable;
        }

        var dialog = new AvaloniaPermissionDialog(request, session);
        await session.RegisterCloseAsync(dialog.CloseFromCoordinatorAsync);
        var pendingOutcome = session.OutcomeOr(PermissionDialogOutcome.Unknown);
        if (pendingOutcome != PermissionDialogOutcome.Unknown)
        {
            return pendingOutcome;
        }

        var result = await dialog.ShowDialog<PermissionDialogOutcome?>(owner);
        return session.OutcomeOr(result ?? PermissionDialogOutcome.Dismissed);
    }
}
