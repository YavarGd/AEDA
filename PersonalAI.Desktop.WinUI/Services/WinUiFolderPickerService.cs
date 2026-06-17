using System.Diagnostics;
using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class WinUiFolderPickerService(Func<WindowId?> getWindowId)
    : IFolderPickerService
{
    public async Task<string?> PickSingleFolderAsync(
        CancellationToken cancellationToken = default)
    {
        WindowId? windowId = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogPickerStage("Creating picker");

            LogPickerStage("Obtaining window id");
            windowId = getWindowId();
            LogPickerStage("Obtaining window id", windowId);

            if (windowId is null)
            {
                throw CreateSafePickerFailure();
            }

            var picker = new FolderPicker(windowId.Value);

            LogPickerStage("Opening picker", windowId);
            var folder = await picker.PickSingleFolderAsync();
            LogPickerStage("Picker returned", windowId);

            if (folder is null)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            LogPickerStage("Reading selected folder path", windowId);
            var path = folder.Path;

            LogPickerStage("Returning selected path", windowId);
            return path;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogPickerStage("Folder picker failed", windowId, exception);
            throw CreateSafePickerFailure();
        }
    }

    private static WorkspaceAccessException CreateSafePickerFailure() =>
        new(
            "folder_picker_failed",
            "Could not open the folder picker.");

    [Conditional("DEBUG")]
    private static void LogPickerStage(
        string stage,
        WindowId? windowId = null,
        Exception? exception = null)
    {
        var uiThreadStatus = Microsoft.UI.Dispatching.DispatcherQueue
            .GetForCurrentThread()
            ?.HasThreadAccess;
        var isWindowIdZero = windowId.HasValue && windowId.Value.Value == 0;
        var exceptionText = exception is null
            ? string.Empty
            : $" exception={exception.GetType().Name} hresult=0x{exception.HResult:X8}";

        Debug.WriteLine(
            $"WorkspaceFolderPicker stage={stage} uiThread={uiThreadStatus?.ToString() ?? "unknown"} windowIdZero={isWindowIdZero}{exceptionText}");
    }
}
