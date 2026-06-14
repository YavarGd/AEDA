using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using PersonalAI.Core.Workspaces;
using PersonalAI.Core.Tools;

namespace PersonalAI.Core.Chat;

public static class ChatToolDefinitionFactory
{
    public static ChatToolDefinition Create(ToolDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        using var document = JsonDocument.Parse(
            CreateSchema(descriptor.InputType).ToJsonString());

        return new ChatToolDefinition(
            descriptor.Id.ToString(),
            descriptor.Description,
            document.RootElement.Clone());
    }

    private static JsonObject CreateSchema(Type inputType)
    {
        var schema = new JsonObject
        {
            ["type"] = "object"
        };
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var property in inputType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead)
            {
                continue;
            }

            var jsonName = ToCamelCase(property.Name);
            properties[jsonName] = CreatePropertySchema(property.PropertyType);

            if (!IsNullable(property))
            {
                required.Add(jsonName);
            }
        }

        schema["properties"] = properties;
        schema["required"] = required;
        schema["additionalProperties"] = false;
        return schema;
    }

    private static JsonObject CreatePropertySchema(Type propertyType)
    {
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (type == typeof(string) || type == typeof(WorkspaceId))
        {
            return new JsonObject { ["type"] = "string" };
        }

        if (type == typeof(bool))
        {
            return new JsonObject { ["type"] = "boolean" };
        }

        if (type == typeof(int) || type == typeof(long))
        {
            return new JsonObject { ["type"] = "integer" };
        }

        if (type.IsEnum)
        {
            var values = Enum.GetNames(type)
                .Select(name => (JsonNode)name)
                .ToArray();

            return new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(values)
            };
        }

        return new JsonObject { ["type"] = "object" };
    }

    private static bool IsNullable(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
        {
            return true;
        }

        return !property.PropertyType.IsValueType;
    }

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
        {
            return value;
        }

        return char.ToLowerInvariant(value[0]) + value[1..];
    }
}
