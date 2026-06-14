using PersonalAI.Core.Permissions;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Workspaces;

public sealed class WorkspacePathResolver(IWorkspaceRegistry registry)
    : IWorkspacePathResolver
{
    public WorkspacePath Resolve(
        WorkspaceId workspaceId,
        string relativePath,
        WorkspacePathKind expectedKind = WorkspacePathKind.Any)
    {
        if (!registry.TryGet(workspaceId, out var workspace))
        {
            throw new WorkspaceAccessException(
                "workspace_not_found",
                "Workspace was not registered.");
        }

        var requestedPath = NormalizeRelativeInput(relativePath);
        var root = workspace.CanonicalRootPath;
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(root, requestedPath));
        }
        catch (Exception exception) when (
            exception is ArgumentException ||
            exception is NotSupportedException ||
            exception is PathTooLongException)
        {
            throw new WorkspaceAccessException(
                "invalid_relative_path",
                "Workspace path was invalid.");
        }

        EnsureInsideRoot(root, fullPath);
        EnsureNoReparsePoints(root, fullPath, expectedKind);
        EnsureExpectedKind(fullPath, expectedKind);

        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".")
        {
            relative = ".";
        }

        return new WorkspacePath(
            workspace,
            NormalizeDisplayRelativePath(relative),
            fullPath,
            CreateResourceScope(workspace.Id, relative));
    }

    public static string CreateResourceScope(
        WorkspaceId workspaceId,
        string relativePath)
    {
        var normalizedRelative = NormalizeDisplayRelativePath(relativePath);
        return PermissionGrantKey.NormalizeResourceScope(
            $"workspace:{workspaceId}:{normalizedRelative}");
    }

    public static string NormalizeDisplayRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
        {
            return ".";
        }

        return relativePath.Replace('\\', '/').Trim('/');
    }

    private static string NormalizeRelativeInput(string? relativePath)
    {
        if (relativePath is null)
        {
            throw new WorkspaceAccessException(
                "invalid_relative_path",
                "Workspace path was invalid.");
        }

        if (relativePath.Contains('\0'))
        {
            throw new WorkspaceAccessException(
                "invalid_relative_path",
                "Workspace path contained an invalid character.");
        }

        if (Path.IsPathRooted(relativePath) ||
            relativePath.StartsWith(@"\\", StringComparison.Ordinal) ||
            relativePath.StartsWith("//", StringComparison.Ordinal))
        {
            throw new WorkspaceAccessException(
                "invalid_relative_path",
                "Workspace path must be relative.");
        }

        if (OperatingSystem.IsWindows() &&
            relativePath.Split(['\\', '/']).Any(segment =>
                segment.Contains(':', StringComparison.Ordinal)))
        {
            throw new WorkspaceAccessException(
                "invalid_relative_path",
                "Workspace path contained an invalid character.");
        }

        if (relativePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new WorkspaceAccessException(
                "invalid_relative_path",
                "Workspace path contained an invalid character.");
        }

        return string.IsNullOrWhiteSpace(relativePath) ? "." : relativePath.Trim();
    }

    private static void EnsureInsideRoot(string root, string fullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var normalizedFullPath = Path.GetFullPath(fullPath).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedRoot, normalizedFullPath, comparison))
        {
            return;
        }

        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        if (!normalizedFullPath.StartsWith(rootWithSeparator, comparison))
        {
            throw new WorkspaceAccessException(
                "path_outside_workspace",
                "Workspace path escaped the approved root.");
        }
    }

    private static void EnsureNoReparsePoints(
        string root,
        string fullPath,
        WorkspacePathKind expectedKind)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var relative = Path.GetRelativePath(rootFull, fullPath);
        if (relative == ".")
        {
            ThrowIfReparsePoint(rootFull);
            return;
        }

        var current = rootFull;
        ThrowIfReparsePoint(current);
        var parts = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < parts.Length; index++)
        {
            current = Path.Combine(current, parts[index]);
            var exists = Directory.Exists(current) || File.Exists(current);
            if (!exists)
            {
                throw new WorkspaceAccessException(
                    expectedKind == WorkspacePathKind.Directory
                        ? "directory_not_found"
                        : expectedKind == WorkspacePathKind.File
                            ? "file_not_found"
                            : "path_not_found",
                    "Workspace path was not found.");
            }

            ThrowIfReparsePoint(current);
        }
    }

    private static void ThrowIfReparsePoint(string path)
    {
        try
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new WorkspaceAccessException(
                    "reparse_point_rejected",
                    "Workspace path uses a reparse point.");
            }
        }
        catch (UnauthorizedAccessException)
        {
            throw new WorkspaceAccessException(
                "access_denied",
                "Workspace path could not be inspected.");
        }
        catch (IOException)
        {
            throw new WorkspaceAccessException(
                "io_error",
                "Workspace path could not be inspected.");
        }
    }

    private static void EnsureExpectedKind(
        string fullPath,
        WorkspacePathKind expectedKind)
    {
        if (expectedKind == WorkspacePathKind.Any)
        {
            return;
        }

        if (expectedKind == WorkspacePathKind.File)
        {
            if (Directory.Exists(fullPath))
            {
                throw new WorkspaceAccessException(
                    "wrong_target_type",
                    "Expected a file but found a directory.");
            }

            if (!File.Exists(fullPath))
            {
                throw new WorkspaceAccessException(
                    "file_not_found",
                    "Workspace file was not found.");
            }

            return;
        }

        if (File.Exists(fullPath))
        {
            throw new WorkspaceAccessException(
                "wrong_target_type",
                "Expected a directory but found a file.");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new WorkspaceAccessException(
                "directory_not_found",
                "Workspace directory was not found.");
        }
    }
}
