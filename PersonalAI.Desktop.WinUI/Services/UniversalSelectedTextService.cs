using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;

namespace PersonalAI.Desktop.WinUI.Services;

public enum SelectedTextCaptureSource
{
    None,
    ExplicitIntegration,
    UiAutomationTextPattern,
    LegacyAccessibility,
    ClipboardCopyFallback
}

public enum SelectedTextCaptureFailure
{
    None,
    NoSelection,
    UnsupportedControl,
    PrivacyBlocked,
    ProtectedControl,
    PasswordControl,
    ElevatedTarget,
    ClipboardBusy,
    ClipboardDidNotChange,
    ClipboardRestoreFailed,
    Timeout,
    Cancelled,
    SafeFailure
}

public sealed record SelectedTextCaptureRequest(
    ActiveWindowReference Foreground,
    PrivacySettings Privacy,
    int MaxCharacters,
    bool AllowClipboardFallback,
    AttachedContextItem? ExplicitContext = null);

public sealed record SelectedTextCaptureResult(
    bool Success,
    string? Text,
    SelectedTextCaptureSource Source,
    string? ApplicationIdentity,
    DateTimeOffset CapturedAtUtc,
    SelectedTextCaptureFailure FailureReason,
    bool ClipboardFallbackUsed,
    bool ClipboardRestorationSucceeded,
    string DiagnosticCode,
    AttachedContextItem? ExplicitContext = null);

public interface IUniversalSelectedTextService
{
    Task<SelectedTextCaptureResult> CaptureAsync(
        SelectedTextCaptureRequest request,
        CancellationToken cancellationToken);
}

public interface IClipboardCopySelectedTextProvider
{
    Task<SelectedTextCaptureResult> CaptureAsync(
        SelectedTextCaptureRequest request,
        CancellationToken cancellationToken);
}

public sealed class UniversalSelectedTextService(
    ISelectedTextContextProvider uiAutomation,
    IClipboardCopySelectedTextProvider clipboardCopy) : IUniversalSelectedTextService
{
    public async Task<SelectedTextCaptureResult> CaptureAsync(
        SelectedTextCaptureRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Foreground.CapturedAtUtc < DateTimeOffset.UtcNow.AddSeconds(-5))
        {
            return Failure(request, SelectedTextCaptureFailure.SafeFailure, "stale-foreground");
        }

        if (PrivacyExclusionMatcher.IsSensitiveWindow(
                request.Foreground.ProcessName,
                request.Foreground.WindowTitle,
                request.Privacy.ExcludedApplications))
        {
            return Failure(request, SelectedTextCaptureFailure.PrivacyBlocked, "privacy-blocked");
        }

        if (request.ExplicitContext is { } explicitContext &&
            AssistContextPolicy.IsMeaningful(explicitContext, DateTimeOffset.UtcNow) &&
            AssistContextPolicy.MatchesForeground(explicitContext, request.Foreground))
        {
            return new SelectedTextCaptureResult(
                true, null, SelectedTextCaptureSource.ExplicitIntegration,
                request.Foreground.ProcessName, DateTimeOffset.UtcNow,
                SelectedTextCaptureFailure.None, false, true, "explicit", explicitContext);
        }

        var uia = await uiAutomation.TryGetSelectedTextAsync(
            request.Foreground, request.Privacy, request.MaxCharacters, cancellationToken);
        if (uia.IsAvailable && uia.IsTrustedForImmediateSubmission)
        {
            return new SelectedTextCaptureResult(
                true, uia.Text, SelectedTextCaptureSource.UiAutomationTextPattern,
                uia.ApplicationIdentity, uia.CapturedAtUtc,
                SelectedTextCaptureFailure.None, false, true, "uia-selection");
        }

        // ponytail: MSAA omitted; add a focused-object provider only if compatibility data proves it returns selections UIA misses.
        if (!request.AllowClipboardFallback)
        {
            return Failure(request, SelectedTextCaptureFailure.UnsupportedControl, "copy-disabled");
        }

        return await clipboardCopy.CaptureAsync(request, cancellationToken);
    }

    private static SelectedTextCaptureResult Failure(
        SelectedTextCaptureRequest request,
        SelectedTextCaptureFailure reason,
        string code) => new(
            false, null, SelectedTextCaptureSource.None,
            request.Foreground.ProcessName, DateTimeOffset.UtcNow,
            reason, false, true, code);
}
