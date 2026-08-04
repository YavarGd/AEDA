using System.Runtime.CompilerServices;

namespace PersonalAI.Tests.Avalonia.Tasks;

public sealed class AvaloniaAedaTaskCenterViewTests
{
    [Fact]
    public void ViewPresentsEveryTaskBucketAndSafeTimelineFields()
    {
        var source = ReadSource("Views", "Tasks", "TaskCenterView.axaml");

        Assert.Contains("ItemsSource=\"{Binding ActiveTasks}\"", source);
        Assert.Contains("ItemsSource=\"{Binding WaitingApprovals}\"", source);
        Assert.Contains("ItemsSource=\"{Binding RecentTasks}\"", source);
        Assert.Contains("ItemsSource=\"{Binding FailedOrCancelledTasks}\"", source);
        Assert.Contains("Text=\"{Binding Summary}\"", source);
        Assert.Contains("Text=\"{Binding Detail}\"", source);
        Assert.Contains("ItemsSource=\"{Binding Links}\"", source);
        Assert.Contains("Text=\"{Binding SafeSummary}\"", source);
        Assert.Contains("Text=\"{Binding SafeUnavailableReason}\"", source);
    }

    [Fact]
    public void ShellExposesOneAccessibleTaskCenterRoute()
    {
        var source = ReadSource("MainWindow.axaml");

        Assert.Contains("x:Name=\"TaskCenterNavItem\"", source);
        Assert.Contains("x:Name=\"TaskCenterRoute\"", source);
        Assert.Contains("AutomationProperties.Name=\"Task Center\"", source);
        Assert.DoesNotContain("Task Center — not available yet", source);
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
        // <repo>/PersonalAI.Tests/Avalonia/Tasks/<this file>
        var tasksDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(tasksDirectory, "..", "..", ".."));
    }
}
