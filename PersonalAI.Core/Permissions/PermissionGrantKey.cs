using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;

namespace PersonalAI.Core.Permissions;

public readonly record struct PermissionGrantKey(
    TaskId TaskId,
    ToolId ToolId,
    ToolPermission Permission,
    PermissionAccessMode AccessMode,
    string NormalizedResourceScope)
{
    public bool IsCacheable => !string.IsNullOrWhiteSpace(NormalizedResourceScope);

    public static PermissionGrantKey Create(
        TaskId taskId,
        ToolId toolId,
        ToolPermission permission,
        PermissionAccessMode accessMode,
        string? resourceScope) =>
        new(
            taskId,
            toolId,
            permission,
            accessMode,
            NormalizeResourceScope(resourceScope));

    public static string NormalizeResourceScope(string? resourceScope)
    {
        if (string.IsNullOrWhiteSpace(resourceScope))
        {
            return string.Empty;
        }

        return resourceScope.Trim()
            .Replace('\\', '/')
            .TrimEnd('/')
            .ToUpperInvariant();
    }
}
