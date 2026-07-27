using PersonalAI.Core.Context;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Ui;

public sealed class AssistActivitySummaryTests
{
    [Fact]
    public void SafeSummary_NeverContainsRawSelectedText()
    {
        var envelope = new AssistContextEnvelope(
            "notepad",
            "notepad",
            "notes.txt",
            AssistContextKind.ApplicationWindow,
            "This is the actual secret selected text that should never appear in events",
            52,
            SelectedTextCaptureSource.ClipboardCopyFallback,
            DateTimeOffset.UtcNow,
            IsTruncated: false,
            IsBlocked: false,
            BlockedReason: null,
            UnderlyingItem: null,
            new Dictionary<string, string>());

        var summary = AssistActivitySummary.CreateSafeSummary(envelope, "copy");

        Assert.DoesNotContain("secret", summary);
        Assert.DoesNotContain("This is the actual", summary);
        Assert.Contains("copy", summary);
        Assert.Contains("52 chars", summary);
        Assert.Contains("notepad", summary);
    }

    [Fact]
    public void SafeSummary_WithNullContext_ReturnsNoContextMessage()
    {
        var summary = AssistActivitySummary.CreateSafeSummary(null, "dismiss");

        Assert.Equal("dismiss (no context)", summary);
    }

    [Fact]
    public void SafeSummary_WithBlockedContext_ReturnsNoContextMessage()
    {
        var blocked = AssistContextEnvelope.Blocked("privacy-blocked", "1password");
        var summary = AssistActivitySummary.CreateSafeSummary(blocked, "dismiss");

        Assert.Equal("dismiss (no context)", summary);
        Assert.DoesNotContain("privacy-blocked", summary);
    }

    [Fact]
    public void SafeSummary_WithNoSelectedText_OmitsCharCount()
    {
        var envelope = new AssistContextEnvelope(
            "editor",
            "editor",
            null,
            AssistContextKind.VsCodeEditor,
            null,
            0,
            SelectedTextCaptureSource.ExplicitIntegration,
            DateTimeOffset.UtcNow,
            IsTruncated: false,
            IsBlocked: false,
            BlockedReason: null,
            UnderlyingItem: null,
            new Dictionary<string, string>());

        var summary = AssistActivitySummary.CreateSafeSummary(envelope, "explain");

        Assert.Contains("explain", summary);
        Assert.Contains("VsCodeEditor", summary);
        Assert.DoesNotContain("chars", summary);
    }
}
