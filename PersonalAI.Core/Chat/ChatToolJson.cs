using System.Text.Json;
using System.Text.Json.Serialization;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Core.Chat;

public static class ChatToolJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        options.Converters.Add(new WorkspaceIdJsonConverter());
        return options;
    }

    private sealed class WorkspaceIdJsonConverter : JsonConverter<WorkspaceId>
    {
        public override WorkspaceId Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Workspace ID must be a string.");
            }

            return new WorkspaceId(reader.GetString() ?? string.Empty);
        }

        public override void Write(
            Utf8JsonWriter writer,
            WorkspaceId value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
