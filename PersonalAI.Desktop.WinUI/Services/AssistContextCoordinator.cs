using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class AssistContextCoordinator(
    Func<PrivacySettings> getPrivacySettings,
    IUniversalSelectedTextService selectedTextService,
    int selectionCharacterLimit = 12_000,
    bool allowClipboardFallback = false,
    TimeSpan? captureTimeout = null)
{
    private readonly TimeSpan _captureTimeout = captureTimeout ?? TimeSpan.FromMilliseconds(1500);

    public async Task<AssistContextEnvelope> CaptureWithForegroundSnapshotAsync(
        ActiveWindowReference? snapshotForeground,
        CancellationToken cancellationToken = default,
        AttachedContextItem? explicitContext = null)
    {
        if (snapshotForeground is null)
        {
            return AssistContextEnvelope.Empty;
        }

        var privacy = getPrivacySettings();
        if (PrivacyExclusionMatcher.IsSensitiveWindow(
            snapshotForeground.ProcessName,
            snapshotForeground.WindowTitle,
            privacy.ExcludedApplications))
        {
            return AssistContextEnvelope.Blocked(
                "privacy-blocked",
                snapshotForeground.ProcessName ?? string.Empty);
        }

        using var timeoutCts = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_captureTimeout);

        try
        {
            var result = await selectedTextService.CaptureAsync(
                new SelectedTextCaptureRequest(
                    snapshotForeground,
                    privacy,
                    selectionCharacterLimit,
                    allowClipboardFallback,
                    explicitContext),
                timeoutCts.Token);

            return AssistContextEnvelope.FromCaptureResult(snapshotForeground, result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AssistContextEnvelope.Empty;
        }
    }

    public async Task<AssistContextEnvelope> CaptureFromContextServiceAsync(
        ActiveWindowContextService contextService,
        AttachedContextItem? explicitContext = null,
        CancellationToken cancellationToken = default)
    {
        var contextItem = await CaptureWithTimeoutAsync(
            contextService, explicitContext, cancellationToken);

        var foreground = contextService.LastCapturedForeground;

        if (foreground is null || contextItem is null)
        {
            return AssistContextEnvelope.Empty;
        }

        var captureResult = new SelectedTextCaptureResult(
            true,
            contextItem.Metadata.GetValueOrDefault("selectedTextCharacters") is { Length: > 0 } count &&
                int.TryParse(count, out var c) && c > 0
                ? contextItem.Preview
                : null,
            ResolveCaptureSource(contextItem.Type),
            foreground.ProcessName,
            contextItem.CreatedAtUtc,
            SelectedTextCaptureFailure.None,
            false,
            true,
            "coordinator",
            contextItem);

        return AssistContextEnvelope.FromCaptureResult(foreground, captureResult);
    }

    public static AssistContextEnvelope BuildEnvelopeFromItem(
        AttachedContextItem? item,
        ActiveWindowReference? foreground)
    {
        if (item is null || foreground is null)
        {
            return AssistContextEnvelope.Empty;
        }

        var selectedTextLength = 0;
        if (item.Metadata.TryGetValue("selectedTextCharacters", out var countStr) &&
            int.TryParse(countStr, out var count) && count >= 0)
        {
            selectedTextLength = count;
        }

        return new AssistContextEnvelope(
            item.SourceName,
            foreground.ProcessName ?? string.Empty,
            foreground.WindowTitle,
            ResolveContextKind(item.Type),
            item.Preview,
            selectedTextLength,
            ResolveCaptureSource(item.Type),
            item.CreatedAtUtc,
            IsTruncated: false,
            IsBlocked: false,
            BlockedReason: null,
            item,
            new Dictionary<string, string>(item.Metadata));
    }

    private async Task<AttachedContextItem?> CaptureWithTimeoutAsync(
        ActiveWindowContextService contextService,
        AttachedContextItem? explicitContext,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_captureTimeout);

        try
        {
            return await contextService.CaptureAsync(explicitContext, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static AssistContextKind ResolveContextKind(AttachedContextType type) => type switch
    {
        AttachedContextType.VsCodeEditor => AssistContextKind.VsCodeEditor,
        AttachedContextType.ApplicationWindow => AssistContextKind.ApplicationWindow,
        AttachedContextType.Clipboard => AssistContextKind.Clipboard,
        AttachedContextType.Screenshot => AssistContextKind.ScreenText,
        _ => AssistContextKind.None
    };

    private static SelectedTextCaptureSource ResolveCaptureSource(AttachedContextType type) => type switch
    {
        AttachedContextType.VsCodeEditor => SelectedTextCaptureSource.ExplicitIntegration,
        AttachedContextType.Clipboard => SelectedTextCaptureSource.ClipboardCopyFallback,
        AttachedContextType.Screenshot => SelectedTextCaptureSource.ExplicitIntegration,
        AttachedContextType.ApplicationWindow => SelectedTextCaptureSource.UiAutomationTextPattern,
        _ => SelectedTextCaptureSource.UiAutomationTextPattern
    };
}
