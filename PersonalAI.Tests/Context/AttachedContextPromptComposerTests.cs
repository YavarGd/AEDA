using PersonalAI.Core.Chat;
using PersonalAI.Core.Context;
using PersonalAI.Core.Editor;

namespace PersonalAI.Tests.Context;

public sealed class AttachedContextPromptComposerTests
{
    [Fact]
    public void Compose_IncludesEachContextOnceBeforeVisibleUserPrompt()
    {
        var clipboard = AttachedContextFactory.FromClipboardText("clipboard payload");
        var app = AttachedContextFactory.FromActiveApplicationContext(
            CreateApplicationContext());

        var messages = AttachedContextPromptComposer.Compose(
            [new ChatMessage(ChatRole.Assistant, "Earlier answer")],
            "visible prompt",
            [clipboard, app]);

        Assert.Collection(
            messages,
            message => Assert.Equal("Earlier answer", message.Content),
            message =>
            {
                Assert.Equal(ChatRole.System, message.Role);
                Assert.Equal(1, Count(message.Content, "clipboard payload"));
                Assert.Equal(1, Count(message.Content, "Attached active-window context"));
            },
            message =>
            {
                Assert.Equal(ChatRole.User, message.Role);
                Assert.Equal("visible prompt", message.Content);
            });
    }

    [Fact]
    public void Compose_VisibleUserMessageExcludesHiddenContext()
    {
        var clipboard = AttachedContextFactory.FromClipboardText("hidden context");

        var messages = AttachedContextPromptComposer.Compose(
            [],
            "clean prompt",
            [clipboard]);

        var userMessage = messages.Last();
        Assert.Equal(ChatRole.User, userMessage.Role);
        Assert.Equal("clean prompt", userMessage.Content);
        Assert.DoesNotContain("hidden context", userMessage.Content);
    }

    [Fact]
    public void Compose_AttachesScreenshotImageExactlyOnceToVisibleUserMessage()
    {
        var screenshot = AttachedContextFactory.FromScreenshot(
            "Window",
            "notepad",
            "Current window",
            640,
            480,
            "png",
            new ChatImage("image/png", "aW1hZ2U="),
            "data:image/png;base64,thumb",
            temporaryPath: @"C:\Temp\screenshot.png",
            DateTimeOffset.UtcNow);

        var messages = AttachedContextPromptComposer.Compose(
            [],
            "describe this",
            [screenshot]);

        var userMessage = messages.Last();
        var image = Assert.Single(userMessage.Images);
        Assert.Equal("aW1hZ2U=", image.Base64Data);
        Assert.DoesNotContain("screenshot.png", userMessage.Content);
        Assert.DoesNotContain("aW1hZ2U=", userMessage.Content);
    }

    [Fact]
    public void FormatContextBlock_PreservesDeterministicOrdering()
    {
        var first = AttachedContextFactory.FromClipboardText("first");
        var second = AttachedContextFactory.FromClipboardText("second");

        var block = AttachedContextPromptComposer.FormatContextBlock(
            [first, second]);

        Assert.True(
            block.IndexOf("first", StringComparison.Ordinal) <
            block.IndexOf("second", StringComparison.Ordinal));
    }

    [Fact]
    public void FormatContextBlock_PreservesMixedContextOrdering()
    {
        var clipboard = AttachedContextFactory.FromClipboardText("clipboard");
        var screenshot = AttachedContextFactory.FromScreenshot(
            "Window",
            "notepad",
            "Current window",
            640,
            480,
            "png",
            new ChatImage("image/png", "aW1hZ2U="),
            "data:image/png;base64,thumb",
            temporaryPath: null,
            DateTimeOffset.UtcNow);

        var block = AttachedContextPromptComposer.FormatContextBlock(
            [clipboard, screenshot]);

        Assert.True(
            block.IndexOf("clipboard", StringComparison.Ordinal) <
            block.IndexOf("Screenshot context", StringComparison.Ordinal));
    }

    [Fact]
    public void FromEditorContext_MapsEditorMetadataWithoutFullSourcePreview()
    {
        var envelope = CreateEditorEnvelope(selectedText: "line 1\nline 2");

        var item = AttachedContextFactory.FromEditorContext(envelope);

        Assert.Equal(AttachedContextType.VsCodeEditor, item.Type);
        Assert.Equal("Program.cs", item.DisplayTitle);
        Assert.Equal("csharp", item.Metadata["language"]);
        Assert.DoesNotContain("line 1", item.Preview);
        Assert.Contains("line 1", item.ProviderPayload, StringComparison.Ordinal);
    }

    private static ActiveApplicationContext CreateApplicationContext()
    {
        return new ActiveApplicationContext(
            WindowHandle: 123,
            ProcessId: 20,
            ProcessName: "notepad",
            ExecutablePath: null,
            WindowTitle: "Document",
            CapturedSelectedText: null,
            ScreenshotPath: null,
            ScreenshotBytes: null,
            CapturedAtUtc: DateTimeOffset.UtcNow);
    }

    private static EditorContextEnvelope CreateEditorEnvelope(string selectedText)
    {
        return new EditorContextEnvelope(
            ProtocolVersion: 1,
            RequestId: Guid.NewGuid().ToString(),
            ContextSource.Vscode,
            EditorContextCommands.ExplainSelection,
            UserPrompt: null,
            new EditorContext(
                selectedText,
                FullActiveFilePath: @"C:\repo\Program.cs",
                RelativeWorkspacePath: "Program.cs",
                FileName: "Program.cs",
                LanguageId: "csharp",
                Selection: new TextRange(1, 1, 2, 1),
                WorkspaceFolderName: "repo",
                WorkspaceFolderPath: @"C:\repo",
                DocumentVersion: 1,
                IsDirty: false,
                Diagnostics: [],
                TimestampUtc: DateTimeOffset.UtcNow));
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(
                   value,
                   index,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
