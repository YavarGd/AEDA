using PersonalAI.Core.Workspaces;
using Windows.Storage.Pickers;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class WinUiFolderPickerService(Func<nint> getWindowHandle)
    : IFolderPickerService
{
    public async Task<string?> PickSingleFolderAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                getWindowHandle());

            var folder = await picker.PickSingleFolderAsync()
                .AsTask(cancellationToken);
            return folder?.Path;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            throw new WorkspaceAccessException(
                "folder_picker_failed",
                "Folder picker failed.");
        }
    }
}
