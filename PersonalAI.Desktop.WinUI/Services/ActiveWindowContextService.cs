using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Infrastructure.Context;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class ActiveWindowContextService(
    IActiveContextProvider activeContextProvider,
    ForegroundWindowTracker foregroundWindowTracker,
    Func<nint> getOwnWindowHandle,
    Func<PrivacySettings>? getPrivacySettings = null,
    ISelectedTextContextProvider? selectedTextProvider = null,
    Func<int>? getSelectionCharacterLimit = null)
{
    public async Task<AttachedContextItem?> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var ownHandle = getOwnWindowHandle();
        _ = foregroundWindowTracker.CaptureCurrentExternalWindow(ownHandle);

        if (!foregroundWindowTracker.IsLastObservedExternalWindowSafe)
        {
            return null;
        }

        var externalWindow = foregroundWindowTracker.GetLastValidExternalWindow();

        if (externalWindow is null)
        {
            return null;
        }

        var settings = ApplicationSettingsValidator.NormalizePrivacy(
            getPrivacySettings?.Invoke() ?? PrivacySettings.Default);
        string? selectedText = null;
        if (selectedTextProvider is not null)
        {
            var selection = await selectedTextProvider.TryGetSelectedTextAsync(
                externalWindow,
                settings,
                getSelectionCharacterLimit?.Invoke() ?? 12_000,
                cancellationToken);
            if (selection.IsAvailable && selection.IsTrustedForImmediateSubmission)
            {
                selectedText = selection.Text;
            }
        }

        var context = await activeContextProvider.CaptureAsync(
            new ContextCaptureRequest(
                externalWindow.WindowHandle,
                SelectedText: selectedText,
                CaptureScreenshot: false),
            cancellationToken);

        if (context is null)
        {
            return null;
        }

        var sanitized = context with
        {
            ExecutablePath = settings.IncludeExecutablePathInProviderMetadata
                ? context.ExecutablePath
                : null,
            WindowTitle = settings.IncludeWindowTitleInProviderContext
                ? context.WindowTitle
                : null
        };

        return AttachedContextFactory.FromActiveApplicationContext(sanitized);
    }
}
