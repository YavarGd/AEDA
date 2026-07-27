using PersonalAI.Core.Context;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Ui;

public sealed class AssistHandoffPayloadTests
{
    [Fact]
    public void HandoffPayload_PreservesUserRequestAndContext()
    {
        var envelope = new AssistContextEnvelope(
            "notepad",
            "notepad",
            "notes.txt",
            AssistContextKind.ApplicationWindow,
            "selected text",
            13,
            SelectedTextCaptureSource.UiAutomationTextPattern,
            DateTimeOffset.UtcNow,
            IsTruncated: false,
            IsBlocked: false,
            BlockedReason: null,
            UnderlyingItem: null,
            new Dictionary<string, string>());

        var payload = new AssistHandoffPayload(
            "Explain this code",
            envelope,
            "notepad",
            "Here is the explanation...",
            Guid.NewGuid(),
            AssistHandoffDestination.Chat);

        Assert.Equal("Explain this code", payload.UserRequest);
        Assert.NotNull(payload.Context);
        Assert.True(payload.HasContext);
        Assert.Equal("notepad", payload.OriginApplication);
        Assert.Equal("Here is the explanation...", payload.CurrentResponse);
        Assert.NotNull(payload.ConversationId);
        Assert.Equal(AssistHandoffDestination.Chat, payload.Destination);
    }

    [Fact]
    public void HandoffPayload_NullContext_HasNoContext()
    {
        var payload = new AssistHandoffPayload(
            "Help me",
            null,
            null,
            null,
            null,
            AssistHandoffDestination.Chat);

        Assert.False(payload.HasContext);
        Assert.Null(payload.Context);
    }

    [Fact]
    public void HandoffPayload_BlockedContext_HasNoContext()
    {
        var blocked = AssistContextEnvelope.Blocked("privacy-blocked", "1password");
        var payload = new AssistHandoffPayload(
            "Help me",
            blocked,
            "1password",
            null,
            null,
            AssistHandoffDestination.Chat);

        Assert.False(payload.HasContext);
    }

    [Fact]
    public void HandoffPayload_CodeDestination()
    {
        var payload = new AssistHandoffPayload(
            "Fix this bug",
            CreateContext(),
            "VS Code",
            null,
            Guid.NewGuid(),
            AssistHandoffDestination.AedaCode);

        Assert.Equal(AssistHandoffDestination.AedaCode, payload.Destination);
        Assert.True(payload.HasContext);
    }

    [Fact]
    public void HandoffPayload_ResearchDestination()
    {
        var payload = new AssistHandoffPayload(
            "Research this topic",
            CreateContext(),
            "browser",
            null,
            Guid.NewGuid(),
            AssistHandoffDestination.AedaResearch);

        Assert.Equal(AssistHandoffDestination.AedaResearch, payload.Destination);
    }

    [Fact]
    public void HandoffPayload_MemoryDestination()
    {
        var payload = new AssistHandoffPayload(
            "Remember this",
            CreateContext(),
            "notepad",
            null,
            null,
            AssistHandoffDestination.AedaMemory);

        Assert.Equal(AssistHandoffDestination.AedaMemory, payload.Destination);
    }

    [Fact]
    public void HandoffPayload_SafeActivitySummary_NeverContainsRawText()
    {
        var envelope = new AssistContextEnvelope(
            "notepad",
            "notepad",
            "notes.txt",
            AssistContextKind.ApplicationWindow,
            "This is secret selected text",
            26,
            SelectedTextCaptureSource.ClipboardCopyFallback,
            DateTimeOffset.UtcNow,
            IsTruncated: false,
            IsBlocked: false,
            BlockedReason: null,
            UnderlyingItem: null,
            new Dictionary<string, string>());

        var payload = new AssistHandoffPayload(
            "Copy this",
            envelope,
            "notepad",
            "Copied",
            Guid.NewGuid(),
            AssistHandoffDestination.Chat);

        var summary = payload.SafeActivitySummary;
        Assert.DoesNotContain("secret", summary);
        Assert.DoesNotContain("This is", summary);
        Assert.Contains("26 chars", summary);
        Assert.Contains("chat", summary);
    }

    [Fact]
    public void HandoffPayload_SafeActivitySummary_WithBlockedContext_ShowsNoContext()
    {
        var blocked = AssistContextEnvelope.Blocked("privacy-blocked", "1password");
        var payload = new AssistHandoffPayload(
            "Help",
            blocked,
            "1password",
            null,
            null,
            AssistHandoffDestination.Chat);

        var summary = payload.SafeActivitySummary;
        Assert.Contains("no context", summary);
        Assert.DoesNotContain("privacy-blocked", summary);
    }

    private static AssistContextEnvelope CreateContext() => new(
        "editor",
        "editor",
        null,
        AssistContextKind.ApplicationWindow,
        "selected text",
        13,
        SelectedTextCaptureSource.UiAutomationTextPattern,
        DateTimeOffset.UtcNow,
        IsTruncated: false,
        IsBlocked: false,
        BlockedReason: null,
        UnderlyingItem: null,
        new Dictionary<string, string>());
}
