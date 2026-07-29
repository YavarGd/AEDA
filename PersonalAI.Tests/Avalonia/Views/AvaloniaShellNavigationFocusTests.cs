using System.Runtime.CompilerServices;

namespace PersonalAI.Tests.Avalonia.Views;

/// <summary>
/// Focus routing lives in window code-behind that cannot be constructed without an
/// Avalonia application, so this asserts the call sites directly in the source.
/// </summary>
public sealed class AvaloniaShellNavigationFocusTests
{
    [Fact]
    public void SelectionDrivenRouting_DoesNotMoveFocusIntoContent()
    {
        var source = ReadMainWindowSource();
        var handler = ExtractMethod(
            source,
            "private void OnNavigationSelectionChanged",
            "private void OnShellKeyDown");

        Assert.Contains("Navigate(ShellRoute.Chat, focusContent: false)", handler);
        Assert.Contains("Navigate(ShellRoute.Dashboard, focusContent: false)", handler);
        Assert.DoesNotContain("focusContent: true", handler);
    }

    [Fact]
    public void InitialCtaAndShortcutRouting_MoveFocusIntoContent()
    {
        var source = ReadMainWindowSource();
        var constructor = ExtractMethod(
            source,
            "public MainWindow()",
            "public ShellRoute CurrentRoute");
        var shortcut = ExtractMethod(
            source,
            "private void OnShellKeyDown",
            "\n}");

        // Initial routing and the dashboard call to action.
        Assert.Contains("Navigate(ShellRoute.Dashboard, focusContent: true)", constructor);
        Assert.Contains("Navigate(ShellRoute.Chat, focusContent: true)", constructor);

        // Ctrl+N shortcut.
        Assert.Contains("Navigate(ShellRoute.Chat, focusContent: true)", shortcut);
    }

    [Fact]
    public void NavigateTakesAPlainBooleanRatherThanATriggerEnum()
    {
        var source = ReadMainWindowSource();

        Assert.Contains("private void Navigate(ShellRoute route, bool focusContent)", source);
        Assert.DoesNotContain("ShellNavigationTrigger", source);
        Assert.DoesNotContain("ShellNavigationPolicy", source);
    }

    private static string ExtractMethod(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"'{start}' not found in MainWindow source");

        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        return endIndex < 0
            ? source[startIndex..]
            : source[startIndex..endIndex];
    }

    private static string ReadMainWindowSource([CallerFilePath] string testFilePath = "")
    {
        // <repo>/PersonalAI.Tests/Avalonia/Views/<this file>
        var viewsDirectory = Path.GetDirectoryName(testFilePath)!;
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(viewsDirectory, "..", "..", ".."));
        var path = Path.Combine(
            repositoryRoot,
            "PersonalAI.Desktop.Avalonia",
            "MainWindow.axaml.cs");

        Assert.True(File.Exists(path), $"MainWindow source not found at {path}");
        return File.ReadAllText(path);
    }
}
