using PersonalAI.Core.Context;
using PersonalAI.Core.Chat;

namespace PersonalAI.Tests.Context;

public sealed class AttachedContextCollectionTests
{
    [Fact]
    public void Add_PreventsDuplicateClipboardText()
    {
        var collection = new AttachedContextCollection();
        var first = AttachedContextFactory.FromClipboardText("same text");
        var second = AttachedContextFactory.FromClipboardText("same text");

        Assert.True(collection.Add(first));
        Assert.False(collection.Add(second));
        Assert.Single(collection.Items);
    }

    [Fact]
    public void FromClipboardText_RejectsEmptyClipboardText()
    {
        Assert.Throws<ArgumentException>(
            () => AttachedContextFactory.FromClipboardText("   "));
    }

    [Fact]
    public void CreatePreview_TruncatesLongText()
    {
        var preview = AttachedContextFactory.CreatePreview(new string('a', 240));

        Assert.True(preview.Length < 240);
        Assert.EndsWith("...", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_PreventsDuplicateApplicationWindowContext()
    {
        var collection = new AttachedContextCollection();
        var first = AttachedContextFactory.FromActiveApplicationContext(
            CreateApplicationContext(processId: 10, title: "Document"));
        var second = AttachedContextFactory.FromActiveApplicationContext(
            CreateApplicationContext(processId: 10, title: "Document"));

        Assert.True(collection.Add(first));
        Assert.False(collection.Add(second));
    }

    [Fact]
    public void Remove_RemovesOnlyRequestedContext()
    {
        var collection = new AttachedContextCollection();
        var clipboard = AttachedContextFactory.FromClipboardText("clipboard");
        var app = AttachedContextFactory.FromActiveApplicationContext(
            CreateApplicationContext(processId: 11, title: "Window"));

        collection.Add(clipboard);
        collection.Add(app);

        Assert.True(collection.Remove(clipboard.Id));
        var remaining = Assert.Single(collection.Items);
        Assert.Equal(app.Id, remaining.Id);
    }

    [Fact]
    public void Clear_RemovesAllContexts()
    {
        var collection = new AttachedContextCollection();
        collection.Add(AttachedContextFactory.FromClipboardText("clipboard"));
        collection.Add(AttachedContextFactory.FromActiveApplicationContext(
            CreateApplicationContext(processId: 12, title: "Window")));

        collection.Clear();

        Assert.Empty(collection.Items);
    }

    [Fact]
    public void Snapshot_IsImmutableAfterCollectionChanges()
    {
        var collection = new AttachedContextCollection();
        collection.Add(AttachedContextFactory.FromClipboardText("clipboard"));

        var snapshot = collection.Snapshot();
        collection.Clear();

        Assert.Single(snapshot);
        Assert.Empty(collection.Items);
    }

    [Fact]
    public void FromScreenshot_CreatesImageContextWithThumbnailMetadata()
    {
        var item = AttachedContextFactory.FromScreenshot(
            "Window",
            "notepad",
            "Current window",
            800,
            600,
            "png",
            new ChatImage("image/png", "aW1hZ2U="),
            "data:image/png;base64,thumb",
            temporaryPath: null,
            DateTimeOffset.UtcNow);

        Assert.Equal(AttachedContextType.Screenshot, item.Type);
        Assert.Equal("data:image/png;base64,thumb", item.ThumbnailDataUri);
        Assert.Equal("800", item.Metadata["width"]);
        Assert.Equal("600", item.Metadata["height"]);
        var image = Assert.Single(item.Images);
        Assert.Equal("image/png", image.MediaType);
    }

    [Fact]
    public void ChatModelCapabilityService_BlocksUnknownTextOnlyModel()
    {
        Assert.False(ChatModelCapabilityService.SupportsImages("llama3"));
    }

    [Fact]
    public void ChatModelCapabilityService_AllowsConfiguredVisionModel()
    {
        Assert.True(ChatModelCapabilityService.SupportsImages("llava:latest"));
    }

    private static ActiveApplicationContext CreateApplicationContext(
        uint processId,
        string title)
    {
        return new ActiveApplicationContext(
            WindowHandle: 123,
            processId,
            "notepad",
            ExecutablePath: @"C:\Windows\notepad.exe",
            title,
            CapturedSelectedText: null,
            ScreenshotPath: null,
            ScreenshotBytes: null,
            DateTimeOffset.UtcNow);
    }
}
