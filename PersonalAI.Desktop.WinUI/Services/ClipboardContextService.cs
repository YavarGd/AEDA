using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using Windows.ApplicationModel.DataTransfer;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class ClipboardContextService(
    Func<ContextSettings>? getContextSettings = null)
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

            var settings = ApplicationSettingsValidator.NormalizeContext(
                getContextSettings?.Invoke() ?? ContextSettings.Default);
            var clipped = text.Length > settings.MaxIndividualClipboardCharacters
                ? text[..settings.MaxIndividualClipboardCharacters]
                : text;

            return AttachedContextFactory.FromClipboardText(clipped);
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
