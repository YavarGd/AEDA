using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.Avalonia.Platform.Windows.Assist;
using PersonalAI.Desktop.Presentation.Services;
using PersonalAI.Infrastructure.Context;

namespace PersonalAI.Desktop.Avalonia.Views.Assist;

public sealed class AvaloniaAssistContextService(
    IActiveContextProvider activeContextProvider,
    ForegroundWindowTracker foregroundWindowTracker,
    Func<nint> getOwnWindowHandle,
    IApplicationSettingsService settingsService,
    IUniversalSelectedTextService selectedTextService,
    Func<uint, bool>? isHigherIntegrity = null) : IActiveWindowContextService
{
    private readonly Func<uint, bool> _isHigherIntegrity =
        isHigherIntegrity ?? WindowsAssistTargetGuard.IsHigherIntegrity;

    public SelectedTextCaptureResult? LastCaptureResult { get; private set; }

    public async Task<AttachedContextItem?> CaptureAsync(
        AttachedContextItem? explicitContext = null,
        CancellationToken cancellationToken = default)
    {
        LastCaptureResult = null;
        var ownHandle = getOwnWindowHandle();
        _ = foregroundWindowTracker.CaptureCurrentExternalWindow(ownHandle);
        if (!foregroundWindowTracker.IsLastObservedExternalWindowSafe ||
            foregroundWindowTracker.GetLastValidExternalWindow() is not { } externalWindow)
        {
            return null;
        }

        var privacy = ApplicationSettingsValidator.NormalizePrivacy(
            settingsService.Current.Privacy);
        if (ValidateTarget(externalWindow, privacy, _isHigherIntegrity) is { } blocked)
        {
            LastCaptureResult = Failure(externalWindow, blocked);
            return null;
        }

        LastCaptureResult = await selectedTextService.CaptureAsync(
            new SelectedTextCaptureRequest(
                externalWindow,
                privacy,
                settingsService.Current.Context.MaxIndividualClipboardCharacters,
                settingsService.Current.AssistPill.UniversalSelectionFallbackEnabled,
                explicitContext),
            cancellationToken);
        if (LastCaptureResult.ExplicitContext is { } matchedExplicit)
        {
            return matchedExplicit;
        }

        var context = await activeContextProvider.CaptureAsync(
            new ContextCaptureRequest(
                externalWindow.WindowHandle,
                LastCaptureResult.Success ? LastCaptureResult.Text : null,
                CaptureScreenshot: false),
            cancellationToken);
        if (context is null)
        {
            return null;
        }

        var sanitized = context with
        {
            ExecutablePath = privacy.IncludeExecutablePathInProviderMetadata
                ? context.ExecutablePath
                : null,
            WindowTitle = privacy.IncludeWindowTitleInProviderContext
                ? context.WindowTitle
                : null
        };
        return AttachedContextFactory.FromActiveApplicationContext(sanitized);
    }

    public static SelectedTextCaptureFailure? ValidateTarget(
        ActiveWindowReference target,
        PrivacySettings privacy,
        Func<uint, bool> isHigherIntegrity)
    {
        if (PrivacyExclusionMatcher.IsSensitiveWindow(
                target.ProcessName,
                target.WindowTitle,
                privacy.ExcludedApplications))
        {
            return SelectedTextCaptureFailure.PrivacyBlocked;
        }

        return isHigherIntegrity(target.ProcessId)
            ? SelectedTextCaptureFailure.ElevatedTarget
            : null;
    }

    private static SelectedTextCaptureResult Failure(
        ActiveWindowReference target,
        SelectedTextCaptureFailure reason) => new(
            false,
            null,
            SelectedTextCaptureSource.None,
            target.ProcessName,
            DateTimeOffset.UtcNow,
            reason,
            false,
            true,
            reason == SelectedTextCaptureFailure.ElevatedTarget
                ? "elevated-target"
                : "privacy-blocked");
}
