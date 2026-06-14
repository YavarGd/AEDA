using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Workspaces;

public sealed class WorkspaceRegistry : IWorkspaceRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<WorkspaceId, WorkspaceDescriptor> _byId = [];
    private readonly Dictionary<string, WorkspaceId> _byRoot;

    public WorkspaceRegistry()
    {
        _byRoot = new Dictionary<string, WorkspaceId>(PathComparer);
    }

    public WorkspaceDescriptor Register(
        string rootPath,
        string? displayName = null,
        string? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        string canonicalRoot;
        try
        {
            canonicalRoot = NormalizeCanonicalRoot(Path.GetFullPath(rootPath));
        }
        catch (Exception exception) when (
            exception is ArgumentException ||
            exception is NotSupportedException ||
            exception is PathTooLongException)
        {
            throw new WorkspaceAccessException(
                "invalid_workspace_root",
                "Workspace root path was invalid.");
        }

        if (!Directory.Exists(canonicalRoot))
        {
            if (File.Exists(canonicalRoot))
            {
                throw new WorkspaceAccessException(
                    "workspace_root_is_file",
                    "Workspace root must be a directory.");
            }

            throw new WorkspaceAccessException(
                "workspace_not_found",
                "Workspace root was not found.");
        }

        DirectoryInfo rootInfo;
        try
        {
            rootInfo = new DirectoryInfo(canonicalRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException ||
            exception is NotSupportedException ||
            exception is PathTooLongException)
        {
            throw new WorkspaceAccessException(
                "invalid_workspace_root",
                "Workspace root path was invalid.");
        }

        InspectTraversedComponents(canonicalRoot);
        canonicalRoot = NormalizeCanonicalRoot(rootInfo.FullName);

        lock (_gate)
        {
            if (_byRoot.TryGetValue(canonicalRoot, out var existingId))
            {
                return _byId[existingId];
            }

            var descriptor = new WorkspaceDescriptor(
                WorkspaceId.NewId(),
                string.IsNullOrWhiteSpace(displayName)
                    ? rootInfo.Name
                    : displayName.Trim(),
                canonicalRoot,
                DateTimeOffset.UtcNow,
                WorkspaceAccessPolicy.ReadOnly,
                source);
            _byId.Add(descriptor.Id, descriptor);
            _byRoot.Add(canonicalRoot, descriptor.Id);
            return descriptor;
        }
    }

    public bool TryGet(WorkspaceId workspaceId, out WorkspaceDescriptor workspace)
    {
        lock (_gate)
        {
            return _byId.TryGetValue(workspaceId, out workspace!);
        }
    }

    public IReadOnlyList<WorkspaceDescriptor> List()
    {
        lock (_gate)
        {
            return _byId.Values.OrderBy(
                workspace => workspace.DisplayName,
                StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public bool Remove(WorkspaceId workspaceId)
    {
        lock (_gate)
        {
            if (!_byId.Remove(workspaceId, out var descriptor))
            {
                return false;
            }

            _byRoot.Remove(descriptor.CanonicalRootPath);
            return true;
        }
    }

    internal static StringComparer PathComparer =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    internal static string NormalizeCanonicalRoot(string fullPath)
    {
        var normalized = Path.TrimEndingDirectorySeparator(fullPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new WorkspaceAccessException(
                "invalid_workspace_root",
                "Workspace root path was invalid.");
        }

        if (!Path.IsPathFullyQualified(normalized))
        {
            throw new WorkspaceAccessException(
                "invalid_workspace_root",
                "Workspace root path was invalid.");
        }

        return normalized;
    }

    private static void InspectTraversedComponents(string canonicalRoot)
    {
        var filesystemRoot = Path.GetPathRoot(canonicalRoot);
        if (string.IsNullOrWhiteSpace(filesystemRoot))
        {
            throw new WorkspaceAccessException(
                "invalid_workspace_root",
                "Workspace root path was invalid.");
        }

        var current = NormalizeCanonicalRoot(filesystemRoot);
        InspectSingleComponent(current);

        var relative = Path.GetRelativePath(current, canonicalRoot);
        if (relative == ".")
        {
            return;
        }

        foreach (var part in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            InspectSingleComponent(current);
        }
    }

    private static void InspectSingleComponent(string path)
    {
        try
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new WorkspaceAccessException(
                    "reparse_point_rejected",
                    "Workspace root cannot include a reparse point.");
            }
        }
        catch (WorkspaceAccessException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw new WorkspaceAccessException(
                "workspace_access_denied",
                "Workspace root could not be inspected.");
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is System.Security.SecurityException)
        {
            throw new WorkspaceAccessException(
                "workspace_inspection_failed",
                "Workspace root could not be inspected.");
        }
    }
}
