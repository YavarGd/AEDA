using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using PersonalAI.Desktop.Presentation.Services;

namespace PersonalAI.Desktop.Avalonia.Platform.Windows;

public sealed class AvaloniaClipboardWriter(Func<TopLevel?> getTopLevel) : IClipboardWriter
{
    public async Task CopyTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var clipboard = getTopLevel()?.Clipboard ??
                throw new InvalidOperationException("Clipboard is unavailable.");
            await clipboard.SetTextAsync(text);
            cancellationToken.ThrowIfCancellationRequested();
            await clipboard.FlushAsync();
        }
        catch (Exception exception) when (
            exception is ExternalException or InvalidOperationException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Text could not be copied to the clipboard.", exception);
        }
    }
}
