using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalAI.Core.Editor;

public static class EditorContextProtocol
{
    public const int SupportedProtocolVersion = 1;
    public const int MaxMessageBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static EditorContextEnvelope Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > MaxMessageBytes)
        {
            throw new EditorContextProtocolException("Message exceeds the maximum size.");
        }

        EditorContextEnvelope? envelope;

        try
        {
            envelope = JsonSerializer.Deserialize<EditorContextEnvelope>(
                utf8Json,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new EditorContextProtocolException(
                "Message is not valid JSON.",
                exception);
        }

        if (envelope is null)
        {
            throw new EditorContextProtocolException("Message was empty.");
        }

        Validate(envelope);
        return envelope;
    }

    public static string Serialize(EditorContextEnvelope envelope)
    {
        Validate(envelope);
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public static byte[] SerializeUtf8(EditorContextEnvelope envelope)
    {
        return Encoding.UTF8.GetBytes(Serialize(envelope));
    }

    private static void Validate(EditorContextEnvelope envelope)
    {
        if (envelope.ProtocolVersion != SupportedProtocolVersion)
        {
            throw new EditorContextProtocolException(
                $"Unsupported protocol version {envelope.ProtocolVersion}.");
        }

        if (string.IsNullOrWhiteSpace(envelope.RequestId))
        {
            throw new EditorContextProtocolException("RequestId is required.");
        }

        if (envelope.Source != ContextSource.Vscode)
        {
            throw new EditorContextProtocolException("Only VS Code context is supported.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Command))
        {
            throw new EditorContextProtocolException("Command is required.");
        }

        if (envelope.Command.Equals(
                EditorContextCommands.OpenPersonalAi,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (envelope.Context is null)
        {
            throw new EditorContextProtocolException("Context is required.");
        }

        if (envelope.Context.SelectedText?.Length >
            EditorContextProtocolLimits.MaxSelectedTextCharacters)
        {
            throw new EditorContextProtocolException("Selected text is too large.");
        }
    }
}
