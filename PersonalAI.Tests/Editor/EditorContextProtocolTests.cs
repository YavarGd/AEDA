using System.Text;
using PersonalAI.Core.Editor;

namespace PersonalAI.Tests.Editor;

public sealed class EditorContextProtocolTests
{
    [Fact]
    public void Deserialize_ReadsValidEnvelope()
    {
        var envelope = CreateEnvelope();
        var json = EditorContextProtocol.Serialize(envelope);

        var parsed = EditorContextProtocol.Deserialize(Encoding.UTF8.GetBytes(json));

        Assert.Equal("request-1", parsed.RequestId);
        Assert.Equal(ContextSource.Vscode, parsed.Source);
        Assert.Equal("Program.cs", parsed.Context?.FileName);
    }

    [Fact]
    public void Deserialize_RejectsUnsupportedProtocolVersion()
    {
        var json = """
            {
              "protocolVersion": 999,
              "requestId": "request-1",
              "source": "vscode",
              "command": "askAboutSelection",
              "context": {}
            }
            """;

        var exception = Assert.Throws<EditorContextProtocolException>(
            () => EditorContextProtocol.Deserialize(Encoding.UTF8.GetBytes(json)));

        Assert.Contains("Unsupported protocol version", exception.Message);
    }

    [Fact]
    public void Deserialize_RejectsOversizedMessages()
    {
        var oversized = new byte[EditorContextProtocol.MaxMessageBytes + 1];

        var exception = Assert.Throws<EditorContextProtocolException>(
            () => EditorContextProtocol.Deserialize(oversized));

        Assert.Contains("maximum size", exception.Message);
    }

    [Fact]
    public void Deserialize_RejectsMalformedJson()
    {
        var exception = Assert.Throws<EditorContextProtocolException>(
            () => EditorContextProtocol.Deserialize(Encoding.UTF8.GetBytes("{broken")));

        Assert.Contains("valid JSON", exception.Message);
    }

    [Fact]
    public void Deserialize_AllowsMissingOptionalFields()
    {
        var json = """
            {
              "protocolVersion": 1,
              "requestId": "request-1",
              "source": "vscode",
              "command": "askAboutSelection",
              "context": {
                "isDirty": false,
                "diagnostics": [],
                "timestampUtc": "2026-06-09T00:00:00Z"
              }
            }
            """;

        var parsed = EditorContextProtocol.Deserialize(Encoding.UTF8.GetBytes(json));

        Assert.Null(parsed.Context?.SelectedText);
        Assert.Null(parsed.Context?.FullActiveFilePath);
    }

    private static EditorContextEnvelope CreateEnvelope()
    {
        return new EditorContextEnvelope(
            1,
            "request-1",
            ContextSource.Vscode,
            EditorContextCommands.AskAboutSelection,
            "Explain this",
            new EditorContext(
                "selected text",
                @"C:\repo\Program.cs",
                "Program.cs",
                "Program.cs",
                "csharp",
                new TextRange(0, 0, 0, 13),
                "repo",
                @"C:\repo",
                3,
                IsDirty: false,
                [],
                DateTimeOffset.UtcNow));
    }
}
