using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PersonalAI.Core.Permissions;
using PersonalAI.Desktop.WinUI.ViewModels;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class WinUiPermissionBroker(
    DispatcherQueue dispatcherQueue,
    Func<XamlRoot?> getXamlRoot) : IPermissionBroker
{
    private readonly SemaphoreSlim _dialogLock = new(1, 1);

    public async ValueTask<PermissionResponse> RequestPermissionAsync(
        PermissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _dialogLock.WaitAsync(cancellationToken);
        try
        {
            var completion = new TaskCompletionSource<PermissionResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            if (!dispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        completion.SetResult(await ShowDialogAsync(request));
                    }
                    catch (Exception exception)
                    {
                        completion.SetResult(PermissionResponse.Deny(
                            request,
                            $"Approval dialog failed closed: {exception.GetType().Name}."));
                    }
                }))
            {
                return PermissionResponse.Deny(
                    request,
                    "Approval dialog was unavailable.");
            }

            using var _ = cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));
            return await completion.Task;
        }
        finally
        {
            _dialogLock.Release();
        }
    }

    private async Task<PermissionResponse> ShowDialogAsync(PermissionRequest request)
    {
        var xamlRoot = getXamlRoot();
        if (xamlRoot is null)
        {
            return PermissionResponse.Deny(
                request,
                "Approval dialog was unavailable.");
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

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"Approve tool: {viewModel.Title}",
            Content = content,
            PrimaryButtonText = "Allow once",
            SecondaryButtonText = "Deny",
            CloseButtonText = "Allow for task",
            DefaultButton = ContentDialogButton.Secondary
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => PermissionResponse.AllowOnce(request),
            ContentDialogResult.None => PermissionResponse.AllowForTask(request),
            _ => PermissionResponse.Deny(request)
        };
    }
}
