namespace PersonalAI.Tests.Ui;

public sealed class AppLifecycleSourceTests
{
    [Fact]
    public void NormalStartup_ShowsPillWithoutOpeningMainWindow()
    {
        var source = LoadAppSource();
        var startup = Between(
            source,
            "private void StartBackgroundExperience()",
            "private void StartTrayIcon()");

        Assert.Contains("_assistPillWindow?.ShowIdle();", startup);
        Assert.Contains("_isWindowVisible = false;", startup);
        Assert.DoesNotContain("ShowPersonalAi(", startup);
        Assert.DoesNotContain("StartMinimizedToTray", startup);
    }

    [Fact]
    public void MainWindowRemainsExplicitAndAssistWindowIsCreatedOnce()
    {
        var source = LoadAppSource();

        Assert.Contains("() => ShowPersonalAi(repositionIfHidden: true)", source);
        Assert.Equal(1, Count(source, "new AssistPillWindow("));
    }

    [Fact]
    public void ShutdownDisposesShellResourcesAtMostOnce()
    {
        var source = LoadAppSource();
        var dispose = Between(
            source,
            "private void DisposeShellResources()",
            "private static class NativeMessageBox");

        Assert.Contains("if (_shellResourcesDisposed)", dispose);
        Assert.Contains("_shellResourcesDisposed = true;", dispose);
        Assert.Equal(1, Count(dispose, "_assistPillWindow?.Close();"));
    }

    private static string LoadAppSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(
                directory.FullName,
                "PersonalAI.Desktop.WinUI",
                "App.xaml.cs");
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate App.xaml.cs.");
    }

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source[startIndex..endIndex];
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;
}
