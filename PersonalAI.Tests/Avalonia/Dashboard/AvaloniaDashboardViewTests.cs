using System.Runtime.CompilerServices;

namespace PersonalAI.Tests.Avalonia.Dashboard;

public sealed class AvaloniaDashboardViewTests
{
    [Fact]
    public void DashboardCopyDoesNotDescribeMigratedModulesAsDeferred()
    {
        var source = ReadSource("Views", "Dashboard", "DashboardView.axaml");

        // Code, Memory, Research, Task Center and Assist are migrated and reachable from
        // the navigation list, so the Dashboard must not describe them as still pending.
        Assert.DoesNotContain("arrive in later steps", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("later step", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not available yet", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Coming later", source, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Text=\"Local-first assistant. Start in General chat.\"",
            source,
            StringComparison.Ordinal);
    }

    private static string ReadSource(
        params string[] relativePath)
    {
        var repositoryRoot = GetRepositoryRoot();
        var path = Path.Combine(
            [repositoryRoot, "PersonalAI.Desktop.Avalonia", .. relativePath]);
        Assert.True(File.Exists(path), $"Avalonia source not found at {path}");
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot(
        [CallerFilePath] string testFilePath = "")
    {
        // <repo>/PersonalAI.Tests/Avalonia/Dashboard/<this file>
        var dashboardDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(dashboardDirectory, "..", "..", ".."));
    }
}
