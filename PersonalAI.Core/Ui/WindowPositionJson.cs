using System.Text.Json;

namespace PersonalAI.Core.Ui;

public static class WindowPositionJson
{
    public static string Serialize(WindowPosition position) =>
        JsonSerializer.Serialize(position);

    public static WindowPosition? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<WindowPosition>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
