using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PersonalAI.Tests.Avalonia.Assist;

public sealed class AvaloniaAssistMilestone8DesignTests
{
    [Fact]
    public void SharedHostsKeepTheExactStateAndCommandModel()
    {
        var markup = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml");
        var code = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml.cs");
        var state = Read("PersonalAI.Desktop.Presentation", "ViewModels", "AssistPillState.cs");
        var window = Read("PersonalAI.Desktop.Avalonia", "Platform", "Windows", "Assist", "AvaloniaAssistWindow.cs");

        Assert.Contains("x:DataType=\"vm:AssistPillViewModel\"", markup);
        Assert.Contains("_hostMode = AssistViewHostMode.FullModule", code);
        Assert.Contains("new AssistView", window);
        Assert.Contains("HostMode = AssistViewHostMode.CompactWindow", window);
        Assert.Equal(8, Regex.Matches(state, @"^\s+(Hidden|IdlePill|DetectingContext|SpotlightPrompt|StreamingResponse|Completed|Cancelled|Failed),?\r?$", RegexOptions.Multiline).Count);
        Assert.All(new[]
        {
            "Hidden", "IdlePill", "DetectingContext", "SpotlightPrompt",
            "StreamingResponse", "Completed", "Cancelled", "Failed"
        }, value => Assert.Contains(value, state));
        Assert.DoesNotContain("Listening,", state);
        Assert.DoesNotContain("Thinking,", state);
        Assert.DoesNotContain("ActionReady,", state);

        Assert.All(new[]
        {
            "SubmitCommand", "SelectScreenTextCommand", "CancelCommand", "RetryCommand",
            "CopyResponseCommand", "OpenInAedaCommand"
        }, command => Assert.Contains($"Command=\"{{Binding {command}}}\"", markup));
        Assert.Contains("Click=\"OnAskClick\"", markup);
        Assert.Contains("await viewModel.OpenPromptAsync()", code);
        Assert.Equal(8, Count(markup, "<Button x:Name="));
    }

    [Fact]
    public void FullModuleNeverShowsTheNativeLauncher()
    {
        var code = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml.cs");
        var markup = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml");

        Assert.Contains("<Button x:Name=\"CompactPill\"", markup);
        Assert.Contains("CompactPill.IsVisible = compact && idle", code);
        Assert.Contains("var compact = _hostMode == AssistViewHostMode.CompactWindow", code);
        Assert.DoesNotContain("<Button x:Name=\"ExpandedIdentity\"", markup);
    }

    [Fact]
    public void CanonicalGeometryAndSemanticEyeColorsAreReused()
    {
        var markup = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml");
        var styles = Read("PersonalAI.Desktop.Avalonia", "Styles", "Assist.axaml");
        var production = markup + styles;

        Assert.Equal(4, Count(markup, "Data=\"{StaticResource AssistOuterGeometry}\""));
        Assert.Equal(2, Count(markup, "Data=\"{StaticResource AedaEyeGeometry}\""));
        Assert.Contains("Path.launcherGlyph", styles);
        Assert.Contains("Path.identityGlyph", styles);
        Assert.Contains("Fill\" Value=\"{DynamicResource AccentBrush}", styles);
        Assert.Contains("Ellipse.launcherIris", styles);
        Assert.Contains("Ellipse.identityIris", styles);
        Assert.Contains("{DynamicResource ElevatedSurfaceBrush}", styles);
        Assert.Contains("Ellipse.launcherPupil", styles);
        Assert.Contains("Ellipse.identityPupil", styles);
        Assert.DoesNotMatch("#[0-9A-Fa-f]{6}", production);
        Assert.DoesNotContain("DropShadowEffect", production);
        Assert.DoesNotContain("Blur", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Glow", production, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryWorkingAndTerminalStateHasANonColorMark()
    {
        var markup = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml");
        var code = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml.cs");

        Assert.All(new[]
        {
            "AssistListeningMark", "AssistThinkingMark", "AssistActionReadyMark",
            "AssistErrorMark", "AssistCancelledMark"
        }, name => Assert.Contains($"x:Name=\"{name}\"", markup));
        Assert.Equal(3, Count(markup, "Classes=\"identityMark dot"));
        Assert.Contains("AssistListeningMark.IsVisible = state == AssistPillState.DetectingContext", code);
        Assert.Contains("AssistThinkingMark.IsVisible = state == AssistPillState.StreamingResponse", code);
        Assert.Contains("AssistActionReadyMark.IsVisible = state == AssistPillState.Completed", code);
        Assert.Contains("AssistErrorMark.IsVisible = state == AssistPillState.Failed", code);
        Assert.Contains("AssistCancelledMark.IsVisible = state == AssistPillState.Cancelled", code);
    }

    [Fact]
    public void MotionIsLimitedToDetectingOutlineAndStreamingDots()
    {
        var styles = Read("PersonalAI.Desktop.Avalonia", "Styles", "Assist.axaml");
        var code = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml.cs");

        Assert.Equal(4, Count(styles, "IterationCount=\"Infinite\""));
        Assert.Contains("Grid.assistIdentity.motion.listening Path.identityAura", styles);
        Assert.Contains("Grid.assistIdentity.motion.thinking Ellipse.dotOne", styles);
        Assert.Contains("Grid.assistIdentity.motion.thinking Ellipse.dotTwo", styles);
        Assert.Contains("Grid.assistIdentity.motion.thinking Ellipse.dotThree", styles);
        Assert.DoesNotContain("Grid.assistIdentity.motion.thinking Path.identityAura", styles);
        Assert.DoesNotContain("motion.actionReady", styles);
        Assert.DoesNotContain("motion.cancelled", styles);
        Assert.DoesNotContain("motion.error", styles);
        Assert.Contains("new UISettings().AnimationsEnabled", code);
        Assert.Contains("ExpandedIdentity.Classes.Set(\"motion\", _animationsEnabled)", code);
        Assert.Contains("return false", Slice(code, "private static bool ReadAnimationsEnabled", "\n    }\n}"));
    }

    [Fact]
    public void StatusTextIsTheOnlyUnlabeledPoliteLiveRegion()
    {
        var markup = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml");
        var status = Slice(markup, "x:Name=\"StatusText\"", "/>");
        var response = Slice(markup, "x:Name=\"ResponseText\"", "/>");

        Assert.Equal(1, Count(markup, "AutomationProperties.LiveSetting=\"Polite\""));
        Assert.Contains("Text=\"{Binding StatusText}\"", status);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", status);
        Assert.DoesNotContain("AutomationProperties.Name", status);
        Assert.DoesNotContain("AutomationProperties.LabeledBy", status);
        Assert.DoesNotContain("AutomationProperties.HelpText", status);
        Assert.Contains("<SelectableTextBlock", markup);
        Assert.Contains("Text=\"{Binding Response}\"", response);
        Assert.Contains("AutomationProperties.Name=\"Assist response\"", response);
        Assert.DoesNotContain("LiveSetting", response);
    }

    [Fact]
    public void ResponseRegionExistsOnlyForRealResponseContent()
    {
        var markup = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml");
        var code = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml.cs");
        var surface = Slice(markup, "<Grid x:Name=\"ResponseSurface\"", "</UserControl>");

        Assert.Contains("IsVisible=\"{Binding IsResponseSurface}\"", surface);
        Assert.Contains("x:Name=\"ResponseRegion\"", surface);
        Assert.Contains("IsVisible=\"{Binding HasResponse}\"", surface);
        Assert.Contains("ResponseSurface.RowDefinitions[0].Height = _viewModel?.HasResponse == true", code);
        Assert.Contains("new GridLength(0)", code);
        Assert.Contains("RowDefinitions=\"*,Auto\"", surface);
        Assert.Contains("TextWrapping=\"Wrap\"", surface);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", surface);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", surface);

        var scrollerEnd = surface.IndexOf("</ScrollViewer>", StringComparison.Ordinal);
        var actions = surface.IndexOf("x:Name=\"ResponseActions\"", StringComparison.Ordinal);
        Assert.True(scrollerEnd >= 0 && actions > scrollerEnd);
        Assert.Contains("<WrapPanel x:Name=\"ResponseActions\"", surface);
    }

    [Fact]
    public void ActionEligibilityRemainsIndependentAndStateCorrect()
    {
        var markup = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml");
        var viewModel = Read("PersonalAI.Desktop.Presentation", "ViewModels", "AssistPillViewModel.cs");

        Assert.Contains("Command=\"{Binding CancelCommand}\"", markup);
        Assert.Contains("IsVisible=\"{Binding CanCancel}\"", markup);
        Assert.Contains("Command=\"{Binding RetryCommand}\"", markup);
        Assert.Contains("IsVisible=\"{Binding CanRetry}\"", markup);
        Assert.Contains("Command=\"{Binding CopyResponseCommand}\"", markup);
        Assert.Contains("Command=\"{Binding OpenInAedaCommand}\"", markup);
        Assert.Equal(2, Count(markup, "IsVisible=\"{Binding CanShowResponseActions}\""));
        Assert.Contains("public bool CanCancel => IsStreaming", viewModel);
        Assert.Contains("public bool CanShowResponseActions => HasResponse && !IsStreaming", viewModel);
        Assert.Contains("public bool CanRetry => State == AssistPillState.Failed", viewModel);
        Assert.DoesNotContain("CanRetry => State == AssistPillState.Cancelled", viewModel);
    }

    [Fact]
    public void PromptAndKeyboardPathsRemainMinimal()
    {
        var markup = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml");
        var code = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml.cs");

        Assert.Contains("AcceptsReturn=\"False\"", markup);
        Assert.Contains("TextWrapping=\"NoWrap\"", markup);
        Assert.Contains("Command=\"{Binding SubmitCommand}\"", markup);
        Assert.Contains("Command=\"{Binding SelectScreenTextCommand}\"", markup);
        Assert.Contains("e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None", code);
        Assert.Contains("viewModel.SubmitCommand.Execute(null)", code);
        Assert.Contains("e.Key != Key.Escape", code);
        Assert.Contains("!viewModel.CanCancel", code);
        Assert.Contains("viewModel.CancelCommand.Execute(null)", code);
        Assert.DoesNotContain("Microphone", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Model picker", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("History", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RouteUsesOnlyTheShellResponsiveBooleans()
    {
        var code = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml.cs");
        var shell = Read("PersonalAI.Desktop.Avalonia", "MainWindow.axaml.cs");
        var window = Read("PersonalAI.Desktop.Avalonia", "Platform", "Windows", "Assist", "AvaloniaAssistWindow.cs");
        var responsive = Slice(code, "public void ApplyResponsiveMode", "private async void OnAskClick");

        Assert.Contains("public void ApplyResponsiveMode(bool compact, bool medium)", code);
        Assert.Contains("if (_hostMode == AssistViewHostMode.CompactWindow)", responsive);
        Assert.Contains("assistRoute.ApplyResponsiveMode(compact, medium)", shell);
        Assert.Contains("pagePadding = _routeCompact ? 16 : _routeMedium ? 24 : 32", code);
        Assert.Contains("FullSurface.RowSpacing = _routeCompact ? 16 : _routeMedium ? 20 : 24", code);
        Assert.Contains("ModuleTitle.FontSize = _routeCompact ? 22 : _routeMedium ? 24 : 28", code);
        Assert.Contains("StateSurface.Padding = new Thickness(_routeCompact ? 16 : _routeMedium ? 20 : 24)", code);
        Assert.Contains("StateSurface.MaxWidth = _routeCompact || _routeMedium", code);
        Assert.Contains(": 860", code);
        Assert.DoesNotContain("State =", responsive);
        Assert.DoesNotContain("Command.Execute", responsive);
        Assert.DoesNotContain("OpenPromptAsync", responsive);
        Assert.DoesNotContain("ApplyResponsiveMode", window);
        Assert.DoesNotContain("textScale", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FocusChangesOnlyThroughTheExistingEntryAndKeyboardPaths()
    {
        var code = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml.cs");
        var focus = Slice(code, "public void FocusPrimaryAction", "public void ApplyResponsiveMode");
        var stateChanges = Slice(code, "private void OnViewModelPropertyChanged", "public AssistViewHostMode HostMode");

        Assert.Contains("public void FocusPrimaryAction()", code);
        Assert.Contains("IsFallbackInput: true", focus);
        Assert.Contains("PromptBox.Focus()", focus);
        Assert.Contains("IsIdle: true", focus);
        Assert.Contains("AskButton.Focus()", focus);
        Assert.DoesNotContain("CancelButton.Focus", code);
        Assert.DoesNotContain("RetryButton.Focus", code);
        Assert.DoesNotContain("CopyButton.Focus", code);
        Assert.DoesNotContain("OpenInAedaButton.Focus", code);
        Assert.DoesNotContain(".Focus()", stateChanges);
        Assert.Contains("nameof(AssistPillViewModel.HasResponse)", stateChanges);
    }

    [Fact]
    public void DisabledAndStatusOnlyStatesStayReadable()
    {
        var markup = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml");
        var code = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml.cs");

        Assert.Contains("Text=\"Assist is disabled in Settings.\"", markup);
        Assert.Contains("DisabledSurface.IsVisible = !enabled", code);
        Assert.Contains("var enabled = _viewModel?.IsEnabled == true", code);
        Assert.DoesNotContain("Content=\"Settings\"", markup);
        Assert.Contains("StatusRow.IsVisible = enabled", code);
        Assert.DoesNotContain("StateHost.IsVisible = enabled", code);
    }

    [Fact]
    public void NativeWindowSizingAndOwnershipRemainUnchanged()
    {
        var window = Read("PersonalAI.Desktop.Avalonia", "Platform", "Windows", "Assist", "AvaloniaAssistWindow.cs");
        var markup = Read("PersonalAI.Desktop.Avalonia", "Views", "Assist", "AssistView.axaml");

        Assert.Contains("private const double IdleSize = 52", window);
        Assert.Contains("private const double SpotlightWidth = 520", window);
        Assert.Contains("private const double SpotlightHeight = 180", window);
        Assert.Contains("AssistResponseSizingPolicy.Calculate", window);
        Assert.Contains("<Grid Width=\"52\" Height=\"52\">", markup);
        Assert.DoesNotContain("440", markup);
        Assert.DoesNotContain("textScale", markup, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] relativePath)
    {
        var path = Path.Combine([GetRepositoryRoot(), .. relativePath]);
        Assert.True(File.Exists(path), $"Source not found at {path}");
        return File.ReadAllText(path);
    }

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"'{start}' not found");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        return endIndex < 0 ? source[startIndex..] : source[startIndex..endIndex];
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string GetRepositoryRoot([CallerFilePath] string testFilePath = "")
    {
        var assistDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(assistDirectory, "..", "..", ".."));
    }
}
