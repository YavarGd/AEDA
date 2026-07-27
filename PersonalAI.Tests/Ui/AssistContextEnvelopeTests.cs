using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Ui;

public sealed class AssistContextEnvelopeTests
{
    [Fact]
    public void EmptyEnvelopeHasNoContext()
    {
        var envelope = AssistContextEnvelope.Empty;

        Assert.False(envelope.HasContext);
        Assert.Equal(AssistContextKind.None, envelope.ContextKind);
        Assert.True(envelope.IsBlocked is false);
        Assert.Null(envelope.UnderlyingItem);
    }

    [Fact]
    public void BlockedEnvelopeHasBlockedStateAndReason()
    {
        var envelope = AssistContextEnvelope.Blocked("privacy-blocked", "notepad");

        Assert.False(envelope.HasContext);
        Assert.True(envelope.IsBlocked);
        Assert.Equal("privacy-blocked", envelope.BlockedReason);
        Assert.Equal("notepad", envelope.ProcessIdentity);
        Assert.Equal("notepad", envelope.ApplicationLabel);
    }

    [Fact]
    public void FromCaptureResult_BuildsEnvelopeFromSuccessfulCapture()
    {
        var foreground = new ActiveWindowReference(
            100, 42, "notepad", "notes.txt", DateTimeOffset.UtcNow);

        var result = new SelectedTextCaptureResult(
            true,
            "selected text here",
            SelectedTextCaptureSource.UiAutomationTextPattern,
            "notepad",
            DateTimeOffset.UtcNow,
            SelectedTextCaptureFailure.None,
            false,
            true,
            "uia-selection");

        var envelope = AssistContextEnvelope.FromCaptureResult(foreground, result);

        Assert.True(envelope.HasContext);
        Assert.Equal("notepad", envelope.ApplicationLabel);
        Assert.Equal("notepad", envelope.ProcessIdentity);
        Assert.Equal("notes.txt", envelope.AllowedTitle);
        Assert.Equal(AssistContextKind.ApplicationWindow, envelope.ContextKind);
        Assert.Equal("selected text here", envelope.SelectedTextPreview);
        Assert.Equal(18, envelope.SelectedTextLength);
        Assert.Equal(SelectedTextCaptureSource.UiAutomationTextPattern, envelope.CaptureMethod);
        Assert.False(envelope.IsTruncated);
        Assert.False(envelope.IsBlocked);
        Assert.Null(envelope.BlockedReason);
    }

    [Fact]
    public void FromCaptureResult_UsesExplicitContextType_ForVsCode()
    {
        var foreground = new ActiveWindowReference(
            100, 42, "Code", "Program.cs - repo", DateTimeOffset.UtcNow);

        var explicitContext = new AttachedContextItem(
            Guid.NewGuid(),
            AttachedContextType.VsCodeEditor,
            "VS Code",
            "Program.cs",
            "code preview",
            "payload",
            [],
            null,
            new Dictionary<string, string>
            {
                ["selectedTextCharacters"] = "25",
                ["captureSource"] = "vscode-integration"
            },
            DateTimeOffset.UtcNow,
            "vscode:test");

        var result = new SelectedTextCaptureResult(
            true,
            null,
            SelectedTextCaptureSource.ExplicitIntegration,
            "Code",
            DateTimeOffset.UtcNow,
            SelectedTextCaptureFailure.None,
            false,
            true,
            "explicit",
            explicitContext);

        var envelope = AssistContextEnvelope.FromCaptureResult(foreground, result);

        Assert.Equal(AssistContextKind.VsCodeEditor, envelope.ContextKind);
        Assert.Equal("VS Code", envelope.ApplicationLabel);
        Assert.NotNull(envelope.UnderlyingItem);
        Assert.Equal("vscode-integration", envelope.Metadata["captureSource"]);
    }

    [Fact]
    public void FromCaptureResult_PrivacyBlockedResultProducesBlockedEnvelope()
    {
        var foreground = new ActiveWindowReference(
            100, 42, "1Password", "Vault", DateTimeOffset.UtcNow);

        var result = new SelectedTextCaptureResult(
            false,
            null,
            SelectedTextCaptureSource.None,
            "1Password",
            DateTimeOffset.UtcNow,
            SelectedTextCaptureFailure.PrivacyBlocked,
            false,
            true,
            "privacy-blocked");

        var envelope = AssistContextEnvelope.FromCaptureResult(foreground, result);

        Assert.False(envelope.HasContext);
        Assert.True(envelope.IsBlocked);
        Assert.Equal("privacy-blocked", envelope.BlockedReason);
        Assert.Equal(AssistContextKind.None, envelope.ContextKind);
    }

    [Fact]
    public void FromCaptureResult_UnsupportedControlProducesEmptyContext()
    {
        var foreground = new ActiveWindowReference(
            100, 42, "notepad", "notes.txt", DateTimeOffset.UtcNow);
        var result = new SelectedTextCaptureResult(
            false,
            null,
            SelectedTextCaptureSource.None,
            "notepad",
            DateTimeOffset.UtcNow,
            SelectedTextCaptureFailure.UnsupportedControl,
            false,
            true,
            "copy-disabled");

        var envelope = AssistContextEnvelope.FromCaptureResult(foreground, result);

        Assert.False(envelope.HasContext);
        Assert.False(envelope.IsBlocked);
        Assert.True(AssistContextPreviewModel.FromEnvelope(envelope).IsEmpty);
    }

    [Fact]
    public void FromCaptureResult_PasswordControlResultProducesBlockedEnvelope()
    {
        var foreground = new ActiveWindowReference(
            100, 42, "chrome", "Login", DateTimeOffset.UtcNow);

        var result = new SelectedTextCaptureResult(
            false,
            null,
            SelectedTextCaptureSource.None,
            "chrome",
            DateTimeOffset.UtcNow,
            SelectedTextCaptureFailure.PasswordControl,
            false,
            true,
            "password-field");

        var envelope = AssistContextEnvelope.FromCaptureResult(foreground, result);

        Assert.False(envelope.HasContext);
        Assert.True(envelope.IsBlocked);
        Assert.Equal("password-control", envelope.BlockedReason);
    }

    [Fact]
    public void FromCaptureResult_ProtectedControlResultProducesBlockedEnvelope()
    {
        var foreground = new ActiveWindowReference(
            100, 42, "app", "Secure", DateTimeOffset.UtcNow);

        var result = new SelectedTextCaptureResult(
            false,
            null,
            SelectedTextCaptureSource.None,
            "app",
            DateTimeOffset.UtcNow,
            SelectedTextCaptureFailure.ProtectedControl,
            false,
            true,
            "protected");

        var envelope = AssistContextEnvelope.FromCaptureResult(foreground, result);

        Assert.False(envelope.HasContext);
        Assert.True(envelope.IsBlocked);
        Assert.Equal("protected-control", envelope.BlockedReason);
    }

    [Fact]
    public void FromCaptureResult_ElevatedTargetResultProducesBlockedEnvelope()
    {
        var foreground = new ActiveWindowReference(
            100, 42, "admin", " elevated", DateTimeOffset.UtcNow);

        var result = new SelectedTextCaptureResult(
            false,
            null,
            SelectedTextCaptureSource.None,
            "admin",
            DateTimeOffset.UtcNow,
            SelectedTextCaptureFailure.ElevatedTarget,
            false,
            true,
            "elevated");

        var envelope = AssistContextEnvelope.FromCaptureResult(foreground, result);

        Assert.False(envelope.HasContext);
        Assert.True(envelope.IsBlocked);
        Assert.Equal("elevated-target", envelope.BlockedReason);
    }

    [Fact]
    public void FromCaptureResult_ClipboardFallbackSourceMapsCorrectly()
    {
        var foreground = new ActiveWindowReference(
            100, 42, "chrome", "Gmail", DateTimeOffset.UtcNow);

        var result = new SelectedTextCaptureResult(
            true,
            "email body",
            SelectedTextCaptureSource.ClipboardCopyFallback,
            "chrome",
            DateTimeOffset.UtcNow,
            SelectedTextCaptureFailure.None,
            true,
            true,
            "clipboard-selection");

        var envelope = AssistContextEnvelope.FromCaptureResult(foreground, result);

        Assert.True(envelope.HasContext);
        Assert.Equal(AssistContextKind.ApplicationWindow, envelope.ContextKind);
        Assert.Equal(SelectedTextCaptureSource.ClipboardCopyFallback, envelope.CaptureMethod);
    }
}
