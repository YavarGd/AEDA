using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PersonalAI.Core.Permissions;
using PersonalAI.Desktop.WinUI.ViewModels;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class WinUiPermissionBroker(
    DispatcherQueue dispatcherQueue,
    Func<XamlRoot?> getXamlRoot) : IPermissionBroker, IDisposable
{
    private readonly PermissionDialogCoordinator _coordinator = new();

    public async ValueTask<PermissionResponse> RequestPermissionAsync(
        PermissionRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _coordinator.RequestPermissionAsync(
            request,
            PresentOnUiThreadAsync,
            cancellationToken);
    }

    public void Dispose()
    {
        _coordinator.Dispose();
    }

    private async Task<PermissionDialogOutcome> PresentOnUiThreadAsync(
        PermissionRequest request,
        PermissionDialogSession session)
    {
        var completion = new TaskCompletionSource<PermissionDialogOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    completion.TrySetResult(await ShowDialogAsync(request, session));
                }
                catch
                {
                    completion.TrySetResult(PermissionDialogOutcome.Error);
                }
            }))
        {
            return PermissionDialogOutcome.Unavailable;
        }

        return await completion.Task;
    }

    private async Task<PermissionDialogOutcome> ShowDialogAsync(
        PermissionRequest request,
        PermissionDialogSession session)
    {
        var xamlRoot = getXamlRoot();
        if (xamlRoot is null)
        {
            return PermissionDialogOutcome.Unavailable;
        }

        var viewModel = new PermissionRequestViewModel(request);
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = viewModel.Explanation,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock { Text = $"Risk: {viewModel.Risk}" });
        content.Children.Add(new TextBlock
        {
            Text = $"Permissions: {viewModel.Permissions}",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = $"Scope: {viewModel.Scope}",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock { Text = viewModel.Impact });

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var allowOnce = new Button { Content = "Allow once" };
        var allowForTask = new Button { Content = "Allow for task" };
        var cancelTask = new Button { Content = "Cancel task" };
        var deny = new Button { Content = "Deny" };
        buttonRow.Children.Add(allowOnce);
        buttonRow.Children.Add(allowForTask);
        buttonRow.Children.Add(cancelTask);
        buttonRow.Children.Add(deny);
        content.Children.Add(buttonRow);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"Approve tool: {viewModel.Title}",
            Content = content,
            DefaultButton = ContentDialogButton.None
        };

        await session.RegisterCloseAsync(() => HideDialogOnUiThreadAsync(dialog));

        allowOnce.Click += (_, _) =>
        {
            if (session.TrySetOutcome(PermissionDialogOutcome.AllowOnce))
            {
                dialog.Hide();
            }
        };
        allowForTask.Click += (_, _) =>
        {
            if (session.TrySetOutcome(PermissionDialogOutcome.AllowForTask))
            {
                dialog.Hide();
            }
        };
        cancelTask.Click += (_, _) =>
        {
            if (session.TrySetOutcome(PermissionDialogOutcome.CancelTask))
            {
                dialog.Hide();
            }
        };
        deny.Click += (_, _) =>
        {
            if (session.TrySetOutcome(PermissionDialogOutcome.Deny))
            {
                dialog.Hide();
            }
        };

        _ = await dialog.ShowAsync();
        return session.OutcomeOr(PermissionDialogOutcome.Dismissed);
    }

    private Task HideDialogOnUiThreadAsync(ContentDialog dialog)
    {
        if (dispatcherQueue.HasThreadAccess)
        {
            dialog.Hide();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    dialog.Hide();
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }))
        {
            completion.TrySetResult();
        }

        return completion.Task;
    }
}
