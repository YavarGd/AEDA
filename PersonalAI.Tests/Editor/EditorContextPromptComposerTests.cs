using PersonalAI.Core.Editor;

namespace PersonalAI.Tests.Editor;

public sealed class EditorContextPromptComposerTests
{
    [Fact]
    public void FormatPromptBlock_SeparatesMetadataAndSelectedCode()
    {
        var envelope = new EditorContextEnvelope(
            1,
            "request-1",
            ContextSource.Vscode,
            EditorContextCommands.ExplainSelection,
            "Explain the selected code.",
            new EditorContext(
                "public void Run() {}",
                @"C:\repo\Program.cs",
                "Program.cs",
                "Program.cs",
                "csharp",
                new TextRange(10, 0, 10, 20),
                "repo",
                @"C:\repo",
                5,
                IsDirty: true,
                [
                    new EditorDiagnostic(
                        "Possible null reference",
                        "warning",
                        null,
                        "csharp",
                        "CS8602")
                ],
                DateTimeOffset.UtcNow));

        var promptBlock = EditorContextPromptComposer.FormatPromptBlock(envelope);

        Assert.Contains("Attached VS Code editor context", promptBlock);
        Assert.Contains("File name: Program.cs", promptBlock);
        Assert.Contains("--- Selected code ---", promptBlock);
        Assert.Contains("public void Run() {}", promptBlock);
        Assert.Contains("Possible null reference", promptBlock);
    }
}
