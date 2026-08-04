using Avalonia.Controls;
using Avalonia.Input.Platform;
using PersonalAI.Desktop.Presentation.Services;

namespace PersonalAI.Desktop.Avalonia.Views.Assist;

/// <summary>
/// Writes text to the system clipboard through the Avalonia <see cref="TopLevel"/> the
/// owning control belongs to. The owner is resolved lazily because the view is created on
/// the UI thread after composition. Failures (no owner yet, no top level, no clipboard, or
/// a platform error) are contained so a copy failure never surfaces as an unhandled
/// exception.
/// </summary>
public sealed class AvaloniaClipboardWriter(Func<Control?> ownerProvider) : IClipboardWriter
{
    public async Task CopyTextAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var owner = ownerProvider();
            if (owner is null)
            {
                return;
            }

            var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            await clipboard.SetTextAsync(text);
        }
        catch
        {
            // Copy failures are surfaced to the user via the view model's status text,
            // not by throwing out of this adapter.
        }
    }
}
