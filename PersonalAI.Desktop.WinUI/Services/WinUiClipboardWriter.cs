using Windows.ApplicationModel.DataTransfer;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class WinUiClipboardWriter : IClipboardWriter
{
    public Task CopyTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
        return Task.CompletedTask;
    }
}
