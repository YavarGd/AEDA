using PersonalAI.Core.Context;
using Windows.ApplicationModel.DataTransfer;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class ClipboardContextService
{
    public async Task<AttachedContextItem?> CaptureTextAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var content = Clipboard.GetContent();

            if (!content.Contains(StandardDataFormats.Text))
            {
                return null;
            }

            var text = await content.GetTextAsync().AsTask(cancellationToken);

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return AttachedContextFactory.FromClipboardText(text);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException ||
            exception is InvalidOperationException ||
            exception is System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
