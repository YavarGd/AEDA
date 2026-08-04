using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PersonalAI.Desktop.Presentation.Services;

namespace PersonalAI.Desktop.Avalonia.Views.Settings;

public sealed class AvaloniaFolderPickerService(Func<TopLevel?> getTopLevel) :
    IFolderPickerService
{
    public async Task<string?> PickSingleFolderAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var storage = getTopLevel()?.StorageProvider;
        if (storage is null || !storage.CanPickFolder)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Choose an AEDA workspace"
        });
        cancellationToken.ThrowIfCancellationRequested();
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}
