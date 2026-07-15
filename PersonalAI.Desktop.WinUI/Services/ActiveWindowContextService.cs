using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Infrastructure.Context;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class ActiveWindowContextService(
    IActiveContextProvider activeContextProvider,
    ForegroundWindowTracker foregroundWindowTracker,
    Func<nint> getOwnWindowHandle,
    Func<PrivacySettings>? getPrivacySettings = null,
    IUniversalSelectedTextService? selectedTextService = null,
    Func<int>? getSelectionCharacterLimit = null,
    Func<bool>? allowClipboardFallback = null)
{
    public SelectedTextCaptureResult? LastCaptureResult { get; private set; }

    public async Task<AttachedContextItem?> CaptureAsync(
        AttachedContextItem? explicitContext = null,
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
        if (selectedTextService is not null)
        {
            LastCaptureResult = await selectedTextService.CaptureAsync(
                new SelectedTextCaptureRequest(
                    externalWindow,
                    settings,
                    getSelectionCharacterLimit?.Invoke() ?? 12_000,
                    allowClipboardFallback?.Invoke() ?? false,
                    explicitContext),
                cancellationToken);
            if (LastCaptureResult.ExplicitContext is { } matchedExplicit)
            {
                return matchedExplicit;
            }

            selectedText = LastCaptureResult.Success ? LastCaptureResult.Text : null;
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
