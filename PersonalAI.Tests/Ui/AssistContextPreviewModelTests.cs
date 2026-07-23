using PersonalAI.Core.Context;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Ui;

public sealed class AssistContextPreviewModelTests
{
    [Fact]
    public void EmptyPreviewModelHasDefaultValues()
    {
        var model = AssistContextPreviewModel.Empty;

        Assert.True(model.IsEmpty);
        Assert.Equal(string.Empty, model.ApplicationLabel);
        Assert.Equal("None", model.ContextType);
        Assert.Equal(string.Empty, model.ShortPreview);
        Assert.Equal(0, model.TextLength);
        Assert.False(model.IsTruncated);
        Assert.False(model.IsBlocked);
        Assert.Null(model.BlockedReason);
        Assert.False(model.IsClearable);
        Assert.Equal(string.Empty, model.SourceIdentity);
    }

    [Fact]
    public void FromEnvelope_ProducesPreviewFromEnvelope()
    {
        var envelope = new AssistContextEnvelope(
            "notepad",
            "notepad",
            "notes.txt",
            AssistContextKind.ApplicationWindow,
            "selected text content",
            19,
            SelectedTextCaptureSource.UiAutomationTextPattern,
            DateTimeOffset.UtcNow,
            IsTruncated: false,
            IsBlocked: false,
            BlockedReason: null,
            UnderlyingItem: null,
            new Dictionary<string, string>());

        var model = AssistContextPreviewModel.FromEnvelope(envelope);

        Assert.False(model.IsEmpty);
        Assert.Equal("notepad", model.ApplicationLabel);
        Assert.Equal("ApplicationWindow", model.ContextType);
        Assert.Equal("selected text content", model.ShortPreview);
        Assert.Equal(19, model.TextLength);
        Assert.False(model.IsTruncated);
        Assert.False(model.IsBlocked);
        Assert.True(model.IsClearable);
        Assert.Equal("notepad", model.SourceIdentity);
    }

    [Fact]
    public void FromEnvelope_TruncatesLongPreview()
    {
        var longText = new string('a', 200);
        var envelope = new AssistContextEnvelope(
            "editor",
            "editor",
            null,
            AssistContextKind.ApplicationWindow,
            longText,
            200,
            SelectedTextCaptureSource.ClipboardCopyFallback,
            DateTimeOffset.UtcNow,
            IsTruncated: true,
            IsBlocked: false,
            BlockedReason: null,
            UnderlyingItem: null,
            new Dictionary<string, string>());

        var model = AssistContextPreviewModel.FromEnvelope(envelope);

        Assert.True(model.IsTruncated);
        Assert.True(model.ShortPreview.Length < 200);
        Assert.EndsWith("...", model.ShortPreview);
    }

    [Fact]
    public void FromEnvelope_BlockedEnvelopeProducesNonClearableModel()
    {
        var envelope = AssistContextEnvelope.Blocked("privacy-blocked", "1password");

        var model = AssistContextPreviewModel.FromEnvelope(envelope);

        Assert.True(model.IsBlocked);
        Assert.Equal("privacy-blocked", model.BlockedReason);
        Assert.False(model.IsClearable);
    }

    [Fact]
    public void FromEnvelope_NullEnvelopeReturnsEmpty()
    {
        var model = AssistContextPreviewModel.FromEnvelope(null!);

        Assert.True(model.IsEmpty);
    }
}
