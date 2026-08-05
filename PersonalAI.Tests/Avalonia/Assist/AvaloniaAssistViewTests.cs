using System.Runtime.CompilerServices;

namespace PersonalAI.Tests.Avalonia.Assist;

public sealed class AvaloniaAssistViewTests
{
    [Fact]
    public void ViewBindsStateDrivenSurfacesAndActions()
    {
        var source = ReadSource("Views", "Assist", "AssistView.axaml");

        Assert.Contains("x:DataType=\"vm:AssistPillViewModel\"", source);
        Assert.Contains("IsVisible=\"{Binding IsIdle}\"", source);
        Assert.Contains("IsVisible=\"{Binding IsFallbackInput}\"", source);
        Assert.Contains("IsVisible=\"{Binding IsResponseSurface}\"", source);
        Assert.Contains("Text=\"{Binding Prompt, Mode=TwoWay}\"", source);
        Assert.Contains("Text=\"{Binding Response}\"", source);
        Assert.Contains("Text=\"{Binding StatusText}\"", source);
        Assert.Contains("Command=\"{Binding SubmitCommand}\"", source);
        Assert.Contains("Command=\"{Binding CancelCommand}\"", source);
        Assert.Contains("Command=\"{Binding RetryCommand}\"", source);
        Assert.Contains("Command=\"{Binding CopyResponseCommand}\"", source);
        Assert.Contains("Command=\"{Binding OpenInAedaCommand}\"", source);
        Assert.Contains("Command=\"{Binding SelectScreenTextCommand}\"", source);
    }

    [Fact]
    public void ViewIsAccessibleAndAvoidsAnimation()
    {
        var source = ReadSource("Views", "Assist", "AssistView.axaml");

        Assert.Contains("AutomationProperties.Name=\"Assist\"", source);
        Assert.Contains("AutomationProperties.Name=\"Assist status\"", source);
        Assert.Contains("AutomationProperties.Name=\"Assist prompt\"", source);
        Assert.Contains("AutomationProperties.Name=\"Assist response\"", source);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", source);
        Assert.DoesNotContain("Animation", source);
        Assert.DoesNotContain("Transitions", source);
    }

    [Fact]
    public void ResponseAreaScrollsWithBoundedHeight()
    {
        var source = ReadSource("Views", "Assist", "AssistView.axaml");

        Assert.Contains("ScrollViewer", source);

        // The response region is bounded by the Star row of the ResponseSurface grid, not by
        // a fixed MaxHeight. A fixed height made the surface demand more room than the
        // Assist window had, pushing the action row outside the window where a pointer could
        // not reach it.
        Assert.Contains("RowDefinitions=\"Auto,*,Auto\"", source);
        Assert.Contains("<ScrollViewer Grid.Row=\"1\"", source);
        Assert.DoesNotContain("MaxHeight=\"420\"", source);
    }

    [Fact]
    public void CompositionRegistersOneAccessibleAssistRoute()
    {
        var source = ReadSource("Composition", "AvaloniaAppComposition.cs");

        Assert.Contains("\"aeda-assist\"", source);
        Assert.Contains("\"Assist\"", source);
        Assert.Contains("new AssistPillHost(", source);
        Assert.Contains("new AssistPillViewModel(", source);
        Assert.Contains("new AvaloniaAssistContextService(", source);
        Assert.Contains("new WindowsUiaSelectedTextProvider()", source);
        Assert.Contains("new WindowsClipboardCopySelectedTextProvider(GetAedaWindowHandle)", source);
        Assert.Contains("new AvaloniaScreenTextCaptureService(", source);
        // Assist reuses the shared Windows clipboard adapter from W3 rather than shipping
        // its own; the adapter takes a TopLevel provider.
        Assert.Contains("using PersonalAI.Desktop.Avalonia.Platform.Windows;", source);
        Assert.Contains("new AvaloniaClipboardWriter(", source);
        Assert.Contains("TopLevel.GetTopLevel(assistView)", source);

        // The Avalonia control must be built by the screen factory, which the shell runs on
        // the UI thread. Composition itself runs before that hand-off, so constructing the
        // view here would create a control off the UI thread.
        Assert.Contains("() => assistView ??= new AssistView()", source);
        Assert.DoesNotContain("var assistView = new AssistView()", source);
    }

    [Fact]
    public void AssistDoesNotShipItsOwnClipboardAdapter()
    {
        // W3 owns the shared implementation at Platform/Windows/AvaloniaClipboardWriter.cs.
        // A second copy under Views/Assist would silently diverge from it.
        var duplicate = Path.Combine(
            GetRepositoryRoot(),
            "PersonalAI.Desktop.Avalonia",
            "Views",
            "Assist",
            "AvaloniaClipboardWriter.cs");

        Assert.False(
            File.Exists(duplicate),
            "Assist must reuse the W3 clipboard adapter, not duplicate it.");

        var shared = Path.Combine(
            GetRepositoryRoot(),
            "PersonalAI.Desktop.Avalonia",
            "Platform",
            "Windows",
            "AvaloniaClipboardWriter.cs");

        Assert.True(File.Exists(shared), "The shared W3 clipboard adapter must remain.");
    }

    [Fact]
    public void ContextAdapterUsesGuardedWindowsCapture()
    {
        var source = ReadSource("Views", "Assist", "AvaloniaAssistContextService.cs");

        Assert.Contains("IActiveWindowContextService", source);
        Assert.Contains("selectedTextService.CaptureAsync", source);
        Assert.Contains("ValidateTarget", source);
        Assert.Contains("CaptureScreenshot: false", source);
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
        // <repo>/PersonalAI.Tests/Avalonia/Assist/<this file>
        var assistDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(assistDirectory, "..", "..", ".."));
    }
}
