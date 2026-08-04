using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;

namespace PersonalAI.Infrastructure.Context;

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

public interface ISelectedTextContextProvider
{
    Task<SelectedTextContextResult> TryGetSelectedTextAsync(
        ActiveWindowReference foreground,
        PrivacySettings privacy,
        int maxCharacters,
        CancellationToken cancellationToken);
}

public sealed record SelectedTextContextResult(
    bool IsAvailable,
    string? Text,
    string SourceType,
    string? ApplicationIdentity,
    DateTimeOffset CapturedAtUtc,
    string? SafeFailureReason,
    bool IsTrustedForImmediateSubmission);

public interface IClipboardCopySelectedTextProvider
{
    Task<SelectedTextCaptureResult> CaptureAsync(
        SelectedTextCaptureRequest request,
        CancellationToken cancellationToken);
}
