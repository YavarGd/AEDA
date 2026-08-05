using System.Runtime.CompilerServices;

namespace PersonalAI.Tests.Avalonia.Assist;

/// <summary>
/// The floating Assist window shrinks to 52x52 while idle. The in-app Assist screen is a
/// full page and is *also* idle by default, so compactness must be selected explicitly by
/// the host rather than inferred from <c>IsIdle</c>.
/// </summary>
public sealed class AvaloniaAssistHostModeTests
{
    [Fact]
    public void AssistWindowExplicitlySelectsCompactHostMode()
    {
        var source = ReadSource(
            "PersonalAI.Desktop.Avalonia", "Platform", "Windows", "Assist", "AvaloniaAssistWindow.cs");

        Assert.Contains("HostMode = AssistViewHostMode.CompactWindow", source);
    }

    [Fact]
    public void InAppAssistScreenKeepsFullModuleMode()
    {
        var composition = ReadSource(
            "PersonalAI.Desktop.Avalonia", "Composition", "AvaloniaAppComposition.cs");

        // The composition-registered screen must not opt into compact mode.
        Assert.Contains("new AssistView()", composition);
        Assert.DoesNotContain("AssistViewHostMode.CompactWindow", composition);

        // FullModule is the default so an unconfigured host stays a full page.
        var view = ReadSource(
            "PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml.cs");
        Assert.Contains(
            "_hostMode = AssistViewHostMode.FullModule",
            view);
    }

    [Fact]
    public void CompactnessIsNotInferredFromIdleAlone()
    {
        var view = ReadSource(
            "PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml.cs");

        // Idle only chooses Pill-vs-surface *within* an already-compact host.
        Assert.Contains("var compact = _hostMode == AssistViewHostMode.CompactWindow", view);
        Assert.Contains("CompactPill.IsVisible = compact && idle", view);
    }

    [Fact]
    public void CompactIdleContentIsStructurallyAbleToFitTheHost()
    {
        var markup = ReadSource(
            "PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml");
        var view = ReadSource(
            "PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml.cs");

        // The Pill stretches to the host with no margin/padding, so a 52x52 window is
        // entirely usable. The 28px module margin would leave negative content area.
        Assert.Contains("x:Name=\"CompactPill\"", markup);
        Assert.Contains("Padding=\"0\"", markup);
        Assert.Contains("Margin=\"0\"", markup);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", markup);
        Assert.Contains("VerticalAlignment=\"Stretch\"", markup);

        // Expanded states in the compact window use tighter margins than the module.
        Assert.Contains("FullSurface.Margin = compact ? new Thickness(12) : new Thickness(28)", view);
        Assert.Contains("ModuleHeader.IsVisible = !compact", view);
    }

    [Fact]
    public void CompactPillKeepsAnAccessibleNameAndAction()
    {
        var markup = ReadSource(
            "PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml");

        // A Button gives keyboard (Space/Enter) and pointer activation plus an
        // invokable automation peer for free.
        Assert.Contains("<Button x:Name=\"CompactPill\"", markup);
        Assert.Contains("AutomationProperties.Name=\"Ask AEDA\"", markup);
        Assert.Contains("Click=\"OnAskClick\"", markup);
    }

    [Fact]
    public void ConversationListItemsExposeTheTitleNotTheRecordToString()
    {
        var markup = ReadSource(
            "PersonalAI.Desktop.Avalonia", "Views", "Chat", "ChatView.axaml");

        // The generated ListBoxItem container supplies the ListItem automation name; without
        // this setter it falls back to Conversation.ToString() and screen readers read the
        // GUID and timestamps aloud.
        Assert.Contains("Selector=\"ListBoxItem\"", markup);
        Assert.Contains("Property=\"AutomationProperties.Name\"", markup);
        Assert.Contains("Value=\"{Binding Title}\"", markup);
    }

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
