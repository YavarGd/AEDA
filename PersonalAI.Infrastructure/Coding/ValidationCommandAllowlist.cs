using PersonalAI.Core.Coding;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Coding;

public sealed class ValidationCommandAllowlist : IValidationCommandAllowlist
{
    private static readonly ValidationCommandTemplate[] Templates =
    [
        new(
            "dotnet-test-personalai",
            "Run PersonalAI tests",
            "dotnet",
            ["test", @"PersonalAI.Tests\PersonalAI.Tests.csproj"],
            TimeSpan.FromMinutes(5),
            @"PersonalAI.Tests\PersonalAI.Tests.csproj"),
        new(
            "dotnet-build-debug",
            "Build solution",
            "dotnet",
            ["build", "PersonalAI.slnx"],
            TimeSpan.FromMinutes(3),
            "PersonalAI.slnx"),
        new(
            "dotnet-build-release",
            "Build solution release",
            "dotnet",
            ["build", "PersonalAI.slnx", "-c", "Release"],
            TimeSpan.FromMinutes(5),
            "PersonalAI.slnx")
    ];

    public IReadOnlyList<ValidationCommandTemplate> ListTemplates() => Templates;

    public bool TryCreateCommand(
        ValidationRunRequest request,
        WorkspaceDescriptor workspace,
        out ValidationCommand command,
        out ValidationFailureReason failureReason)
    {
        command = default!;
        failureReason = ValidationFailureReason.UnknownSafeFailure;

        var template = Templates.FirstOrDefault(item =>
            item.Id.Equals(request.TemplateId, StringComparison.Ordinal));
        if (template is null || IsForbiddenExecutable(template.Executable))
        {
            failureReason = ValidationFailureReason.CommandNotAllowed;
            return false;
        }

        if (template.Arguments.Any(IsUnsafeArgument))
        {
            failureReason = ValidationFailureReason.UnsafeArgument;
            return false;
        }

        if (IsUnsafeRelativePath(request.RelativeWorkingDirectory))
        {
            failureReason = ValidationFailureReason.WorkingDirectoryOutsideWorkspace;
            return false;
        }

        var fullWorkingDirectory = Path.GetFullPath(Path.Combine(
            workspace.CanonicalRootPath,
            request.RelativeWorkingDirectory));
        if (!IsInside(workspace.CanonicalRootPath, fullWorkingDirectory) ||
            !Directory.Exists(fullWorkingDirectory))
        {
            failureReason = ValidationFailureReason.WorkingDirectoryOutsideWorkspace;
            return false;
        }

        var required = Path.GetFullPath(Path.Combine(
            workspace.CanonicalRootPath,
            template.RequiredRelativePath));
        if (!IsInside(workspace.CanonicalRootPath, required) || !File.Exists(required))
        {
            failureReason = ValidationFailureReason.CommandNotAllowed;
            return false;
        }

        command = new ValidationCommand(
            template.Id,
            template.Executable,
            template.Arguments,
            NormalizeRelative(request.RelativeWorkingDirectory),
            template.Timeout);
        return true;
    }

    private static bool IsForbiddenExecutable(string executable) =>
        executable.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
        executable.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
        executable.Equals("bash", StringComparison.OrdinalIgnoreCase) ||
        executable.Equals("sh", StringComparison.OrdinalIgnoreCase) ||
        executable.Equals("git", StringComparison.OrdinalIgnoreCase) ||
        executable.Equals("npm", StringComparison.OrdinalIgnoreCase) ||
        executable.Equals("curl", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnsafeArgument(string argument) =>
        string.IsNullOrWhiteSpace(argument) ||
        argument.Contains('|') ||
        argument.Contains('>') ||
        argument.Contains('<') ||
        argument.Contains("&&", StringComparison.Ordinal) ||
        argument.Contains("..", StringComparison.Ordinal) ||
        argument.Contains("$(", StringComparison.Ordinal);

    private static bool IsUnsafeRelativePath(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        Path.IsPathRooted(value) ||
        value.Contains("..", StringComparison.Ordinal) ||
        value.Contains('\0');

    private static bool IsInside(string root, string fullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var normalizedFull = Path.GetFullPath(fullPath).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedRoot, normalizedFull, comparison) ||
            normalizedFull.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                comparison);
    }

    private static string NormalizeRelative(string value) =>
        string.IsNullOrWhiteSpace(value) || value == "."
            ? "."
            : value.Replace('\\', '/').Trim('/');
}
