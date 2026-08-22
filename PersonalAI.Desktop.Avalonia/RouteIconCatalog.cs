using Avalonia.Media;

namespace PersonalAI.Desktop.Avalonia;

internal static class RouteIconCatalog
{
    private static readonly IReadOnlyDictionary<string, Geometry> Icons =
        new Dictionary<string, Geometry>(StringComparer.Ordinal)
        {
            ["home"] = Geometry.Parse("M3,11 L12,4 L21,11 M5,10 L5,20 L10,20 L10,14 L14,14 L14,20 L19,20 L19,10"),
            ["chat"] = Geometry.Parse("M7,5 L17,5 C19.2,5 21,6.8 21,9 L21,13 C21,15.2 19.2,17 17,17 L11,17 L6,21 L7,17 C4.8,17 3,15.2 3,13 L3,9 C3,6.8 4.8,5 7,5 Z"),
            ["aeda-code"] = Geometry.Parse("M9,6 L3,12 L9,18 M15,6 L21,12 L15,18 M14,3 L10,21"),
            ["aeda-memory"] = Geometry.Parse("M7,4 L17,4 C19.2,4 21,5.8 21,8 L21,16 C21,18.2 19.2,20 17,20 L7,20 C4.8,20 3,18.2 3,16 L3,8 C3,5.8 4.8,4 7,4 Z M8,10 L8,10.1 M12,10 L12,10.1 M16,10 L16,10.1 M8,14 L16,14"),
            ["aeda-research"] = Geometry.Parse("M3,7 L3,19 C3,20.1 3.9,21 5,21 L19,21 C20.1,21 21,20.1 21,19 L21,9 C21,7.9 20.1,7 19,7 L12,7 L10,4 L5,4 C3.9,4 3,4.9 3,6 Z"),
            ["aeda-task-center"] = Geometry.Parse("M4,5 L8,5 L8,9 L4,9 Z M11,7 L21,7 M4,12 L8,12 L8,16 L4,16 Z M11,14 L21,14 M4,19 L8,19 L8,21 L4,21 M11,20 L18,20"),
            ["aeda-assist"] = Geometry.Parse("M12,3 C13.1,3 14,3.6 14.6,4.6 L22,18 C23.1,20 21.7,22 19.5,22 L4.5,22 C2.3,22 0.9,20 2,18 L9.4,4.6 C10,3.6 10.9,3 12,3 Z M12,9 L12,15 M9.5,12 L14.5,12"),
            ["settings"] = Geometry.Parse("M12,8.5 A3.5,3.5 0 1 0 12,15.5 A3.5,3.5 0 1 0 12,8.5 M12,3 L12,5 M12,19 L12,21 M3,12 L5,12 M19,12 L21,12 M5.6,5.6 L7,7 M17,17 L18.4,18.4 M18.4,5.6 L17,7 M7,17 L5.6,18.4"),
            ["module"] = Geometry.Parse("M4,4 L10,4 L10,10 L4,10 Z M14,4 L20,4 L20,10 L14,10 Z M4,14 L10,14 L10,20 L4,20 Z M14,14 L20,14 L20,20 L14,20 Z")
        };

    public static Geometry Get(string route) =>
        Icons.GetValueOrDefault(route, Icons["module"]);
}
