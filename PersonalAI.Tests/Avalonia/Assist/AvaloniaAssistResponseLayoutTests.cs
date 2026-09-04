using System.Runtime.CompilerServices;

namespace PersonalAI.Tests.Avalonia.Assist;

/// <summary>
/// The floating Assist window is bounded. A long response used to grow the scroll region
/// until the Cancel/Retry/Copy/Open-in-AEDA row was laid out below the physical window edge,
/// where a pointer cannot reach it. The response surface must therefore reserve the action
/// row in a fixed Auto row and let only the scroll region absorb leftover height.
/// </summary>
public sealed class AvaloniaAssistResponseLayoutTests
{
    [Fact]
    public void ResponseSurfaceUsesBoundedGridRowsWithActionsInAnAutoRow()
    {
        var markup = ReadAssistView();

        Assert.Contains("<Grid x:Name=\"ResponseSurface\"", markup);
        Assert.Contains("RowDefinitions=\"*,Auto\"", markup);

        var surface = ResponseSurface(markup);

        // Response content occupies the Star row only when present; actions keep an Auto row.
        Assert.Contains("x:Name=\"ResponseRegion\"", surface);
        Assert.Contains("<ScrollViewer Grid.Row=\"1\"", surface);
        Assert.Contains("x:Name=\"ResponseActions\"", surface);
        Assert.Contains("Grid.Row=\"1\"", surface);
    }

    [Fact]
    public void ResponseContentIsInTheStarRowAndScrolls()
    {
        var surface = ResponseSurface(ReadAssistView());

        Assert.Contains("<ScrollViewer Grid.Row=\"1\"", surface);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", surface);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", surface);
        Assert.Contains("x:Name=\"ResponseText\"", surface);

        // A fixed MaxHeight made the surface demand more room than the window had; the Star
        // row must decide the height instead.
        Assert.DoesNotContain("MaxHeight=\"420\"", surface);
    }

    [Fact]
    public void CopyAndOpenInAedaAreOutsideTheScrollingRegion()
    {
        var surface = ResponseSurface(ReadAssistView());

        var scrollerEnd = surface.IndexOf("</ScrollViewer>", StringComparison.Ordinal);
        var copy = surface.IndexOf("x:Name=\"CopyButton\"", StringComparison.Ordinal);
        var open = surface.IndexOf("x:Name=\"OpenInAedaButton\"", StringComparison.Ordinal);

        Assert.True(scrollerEnd >= 0, "response ScrollViewer missing");
        Assert.True(copy > scrollerEnd, "Copy must sit outside the scrolling region");
        Assert.True(open > scrollerEnd, "Open in AEDA must sit outside the scrolling region");
    }

    [Fact]
    public void ResponseSurfaceIsNotHostedByAVerticallyUnboundedStackPanel()
    {
        var markup = ReadAssistView();

        // The state host must pass the finite available height through. A StackPanel here
        // measures children with infinite height, which reintroduces the defect.
        Assert.Contains("<Panel x:Name=\"StateHost\" Grid.Row=\"1\">", markup);

        var host = Between(markup, "<Panel x:Name=\"StateHost\" Grid.Row=\"1\">", "<Grid x:Name=\"ResponseSurface\"");
        Assert.DoesNotContain("<StackPanel Grid.Row=\"1\"", host);
    }

    [Fact]
    public void HostModesRemainIntact()
    {
        var markup = ReadAssistView();
        var code = ReadSource("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml.cs");
        var window = ReadSource(
            "PersonalAI.Desktop.Avalonia", "Platform", "Windows", "Assist", "AvaloniaAssistWindow.cs");

        Assert.Contains("x:Name=\"CompactPill\"", markup);
        Assert.Contains("x:Name=\"FullSurface\"", markup);
        Assert.Contains("HostMode = AssistViewHostMode.CompactWindow", window);
        Assert.Contains("FullSurface.Margin = new Thickness(6)", code);
        Assert.Contains("CompactPill.IsVisible = compact && idle", code);
    }

    [Fact]
    public void ActionCommandsAndAccessibleNamesAreUnchanged()
    {
        var surface = ResponseSurface(ReadAssistView());

        Assert.Contains("Command=\"{Binding CancelCommand}\"", surface);
        Assert.Contains("Command=\"{Binding RetryCommand}\"", surface);
        Assert.Contains("Command=\"{Binding CopyResponseCommand}\"", surface);
        Assert.Contains("Command=\"{Binding OpenInAedaCommand}\"", surface);

        Assert.Contains("AutomationProperties.Name=\"Cancel generation\"", surface);
        Assert.Contains("AutomationProperties.Name=\"Retry\"", surface);
        Assert.Contains("AutomationProperties.Name=\"Copy response\"", surface);
        Assert.Contains("AutomationProperties.Name=\"Open in AEDA\"", surface);
        Assert.Contains("AutomationProperties.Name=\"Assist response\"", surface);
        Assert.DoesNotContain("AutomationProperties.LiveSetting", surface);
        Assert.Equal(1, Count(ReadAssistView(), "AutomationProperties.LiveSetting=\"Polite\""));
    }

    private static string ResponseSurface(string markup) =>
        Between(markup, "<Grid x:Name=\"ResponseSurface\"", "</UserControl>");

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"'{start}' not found");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        return endIndex < 0 ? source[startIndex..] : source[startIndex..endIndex];
    }

    private static string ReadAssistView() =>
        ReadSource("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml");

    private static string ReadSource(params string[] relativePath)
    {
        var path = Path.Combine([GetRepositoryRoot(), .. relativePath]);
        Assert.True(File.Exists(path), $"Source not found at {path}");
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot([CallerFilePath] string testFilePath = "")
    {
        // <repo>/PersonalAI.Tests/Avalonia/Assist/<this file>
        var assistDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(assistDirectory, "..", "..", ".."));
    }
}
