using PersonalAI.Core.Context;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Ui;

public sealed class AssistHandoffServiceTests
{
    [Fact]
    public void Handoff_StoresPayloadInStore()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);
        var envelope = CreateEnvelope();

        service.Handoff(
            "Explain this",
            envelope,
            "notepad",
            "Here is the explanation",
            Guid.NewGuid(),
            AssistHandoffDestination.Chat);

        Assert.True(store.TryRead(out var stored));
        Assert.Equal("Explain this", stored!.UserRequest);
        Assert.Equal(AssistHandoffDestination.Chat, stored.Destination);
        Assert.Same(envelope, stored.Context);
    }

    [Fact]
    public void Handoff_OverwritesPreviousPayload()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);

        service.Handoff("First", null, null, null, null, AssistHandoffDestination.Chat);
        service.Handoff("Second", null, null, null, null, AssistHandoffDestination.AedaCode);

        Assert.True(store.TryRead(out var stored));
        Assert.Equal("Second", stored!.UserRequest);
        Assert.Equal(AssistHandoffDestination.AedaCode, stored.Destination);
    }

    [Fact]
    public void TryConsume_ReturnsPayloadAndClearsStore()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);

        service.Handoff("Request", null, null, null, null, AssistHandoffDestination.AedaMemory);

        var consumed = service.TryConsume();
        Assert.NotNull(consumed);
        Assert.Equal("Request", consumed.UserRequest);

        Assert.False(store.TryRead(out _));
    }

    [Fact]
    public void TryConsume_WhenEmpty_ReturnsNull()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);

        Assert.Null(service.TryConsume());
    }

    [Fact]
    public void TryPeek_DoesNotConsume()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);

        service.Handoff("Request", null, null, null, null, AssistHandoffDestination.AedaResearch);

        Assert.True(service.TryPeek(out var peeked));
        Assert.NotNull(peeked);

        Assert.True(service.TryPeek(out _));
    }

    [Fact]
    public void Store_ClearRemovesPayload()
    {
        var store = new AssistHandoffStore();
        store.Store(new AssistHandoffPayload(
            "Request", null, null, null, null, AssistHandoffDestination.Chat));

        store.Clear();

        Assert.False(store.TryRead(out _));
    }

    [Fact]
    public void Handoff_WithBlockedContext_PreservesBlockedState()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);
        var blocked = AssistContextEnvelope.Blocked("privacy-blocked", "1password");

        service.Handoff("Help", blocked, "1password", null, null, AssistHandoffDestination.Chat);

        Assert.True(store.TryRead(out var stored));
        Assert.False(stored!.HasContext);
        Assert.True(stored.Context!.IsBlocked);
    }

    [Fact]
    public void Handoff_WithAllDestinations_StoresCorrectly()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);
        var envelope = CreateEnvelope();

        foreach (var dest in Enum.GetValues<AssistHandoffDestination>())
        {
            service.Handoff($"Request for {dest}", envelope, "app", null, null, dest);
            Assert.True(store.TryRead(out var stored));
            Assert.Equal(dest, stored!.Destination);
        }
    }

    private static AssistContextEnvelope CreateEnvelope() => new(
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
}
