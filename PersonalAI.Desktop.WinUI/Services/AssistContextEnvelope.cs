using PersonalAI.Core.Context;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed record AssistContextEnvelope(
    string ApplicationLabel,
    string ProcessIdentity,
    string? AllowedTitle,
    AssistContextKind ContextKind,
    string? SelectedTextPreview,
    int SelectedTextLength,
    SelectedTextCaptureSource CaptureMethod,
    DateTimeOffset CapturedAtUtc,
    bool IsTruncated,
    bool IsBlocked,
    string? BlockedReason,
    AttachedContextItem? UnderlyingItem,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static readonly AssistContextEnvelope Empty = new(
        string.Empty,
        string.Empty,
        null,
        AssistContextKind.None,
        null,
        0,
        SelectedTextCaptureSource.None,
        DateTimeOffset.MinValue,
        IsTruncated: false,
        IsBlocked: false,
        BlockedReason: null,
        UnderlyingItem: null,
        new Dictionary<string, string>());

    public bool HasContext => ContextKind != AssistContextKind.None && !IsBlocked;

    public static AssistContextEnvelope Blocked(
        string reason,
        string processIdentity) => new(
        processIdentity,
        processIdentity,
        null,
        AssistContextKind.None,
        null,
        0,
        SelectedTextCaptureSource.None,
        DateTimeOffset.UtcNow,
        IsTruncated: false,
        IsBlocked: true,
        BlockedReason: reason,
        UnderlyingItem: null,
        new Dictionary<string, string>());

    public static AssistContextEnvelope FromCaptureResult(
        ActiveWindowReference foreground,
        SelectedTextCaptureResult captureResult)
    {
        if (captureResult.FailureReason is
            SelectedTextCaptureFailure.PrivacyBlocked or
            SelectedTextCaptureFailure.ProtectedControl or
            SelectedTextCaptureFailure.PasswordControl or
            SelectedTextCaptureFailure.ElevatedTarget)
        {
            return Blocked(
                MapFailureToBlockedReason(captureResult.FailureReason),
                foreground.ProcessName ?? string.Empty);
        }

        var contextKind = ResolveContextKind(captureResult);
        var applicationLabel = ResolveApplicationLabel(foreground, captureResult);
        var selectedText = captureResult.Text;
        var selectedTextLength = selectedText?.Length ?? 0;
        var isTruncated = false;

        var metadata = new Dictionary<string, string>();
        if (captureResult.Source != SelectedTextCaptureSource.None)
        {
            metadata["captureSource"] = captureResult.Source.ToString();
        }
        if (captureResult.ExplicitContext is not null &&
            captureResult.ExplicitContext.Metadata.TryGetValue("captureSource", out var source))
        {
            metadata["captureSource"] = source;
        }

        return new AssistContextEnvelope(
            applicationLabel,
            foreground.ProcessName ?? string.Empty,
            foreground.WindowTitle,
            contextKind,
            selectedText,
            selectedTextLength,
            captureResult.Source,
            captureResult.CapturedAtUtc,
            isTruncated,
            IsBlocked: false,
            BlockedReason: null,
            captureResult.ExplicitContext,
            metadata);
    }

    private static AssistContextKind ResolveContextKind(SelectedTextCaptureResult result)
    {
        if (result.ExplicitContext is { } ctx)
        {
            return ctx.Type switch
            {
                AttachedContextType.VsCodeEditor => AssistContextKind.VsCodeEditor,
                AttachedContextType.ApplicationWindow => AssistContextKind.ApplicationWindow,
                AttachedContextType.Clipboard => AssistContextKind.Clipboard,
                AttachedContextType.Screenshot => AssistContextKind.ScreenText,
                _ => AssistContextKind.ApplicationWindow
            };
        }

        if (result.Source is SelectedTextCaptureSource.None or SelectedTextCaptureSource.LegacyAccessibility)
        {
            return AssistContextKind.None;
        }

        return AssistContextKind.ApplicationWindow;
    }

    private static string ResolveApplicationLabel(
        ActiveWindowReference foreground,
        SelectedTextCaptureResult result)
    {
        if (result.ExplicitContext is { } ctx &&
            !string.IsNullOrWhiteSpace(ctx.SourceName))
        {
            return ctx.SourceName;
        }

        return foreground.ProcessName ?? "Unknown";
    }

    private static string MapFailureToBlockedReason(
        SelectedTextCaptureFailure failure) => failure switch
    {
        SelectedTextCaptureFailure.PrivacyBlocked => "privacy-blocked",
        SelectedTextCaptureFailure.ProtectedControl => "protected-control",
        SelectedTextCaptureFailure.PasswordControl => "password-control",
        SelectedTextCaptureFailure.ElevatedTarget => "elevated-target",
        _ => "capture-failed"
    };
}
