using PersonalAI.Core.Permissions;

namespace PersonalAI.Core.Tools;

public sealed record PermissionRequirement(
    ToolPermission Permission,
    PermissionAccessMode AccessMode,
    string NormalizedResourceScope,
    string UserReadableScope,
    string Explanation,
    bool LeavesMachine = false,
    bool ChangesState = false,
    bool IsReadOnly = true);
