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
        Assert.Contains("MaxHeight=", source);
    }

    [Fact]
    public void CompositionRegistersOneAccessibleAssistRoute()
    {
        var source = ReadSource("Composition", "AvaloniaAppComposition.cs");

        Assert.Contains("\"aeda-assist\"", source);
        Assert.Contains("\"Assist\"", source);
        Assert.Contains("new AssistPillHost(", source);
        Assert.Contains("new AssistPillViewModel(", source);
        Assert.Contains("new AvaloniaAssistContextService()", source);
        Assert.Contains("new AvaloniaScreenTextCaptureService(", source);
        Assert.Contains("new AvaloniaClipboardWriter(() => assistView)", source);

        // The Avalonia control must be built by the screen factory, which the shell runs on
        // the UI thread. Composition itself runs before that hand-off, so constructing the
        // view here would create a control off the UI thread.
        Assert.Contains("() => assistView ??= new AssistView()", source);
        Assert.DoesNotContain("var assistView = new AssistView()", source);
    }

    [Fact]
    public void ContextAdapterDeliberatelyCapturesNoContext()
    {
        var source = ReadSource("Views", "Assist", "AvaloniaAssistContextService.cs");

        Assert.Contains("IActiveWindowContextService", source);
        Assert.Contains("intentionally not wired", source);
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
