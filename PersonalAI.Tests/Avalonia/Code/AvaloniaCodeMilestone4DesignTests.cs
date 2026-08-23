using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PersonalAI.Desktop.Avalonia.Views.Code;

namespace PersonalAI.Tests.Avalonia.Code;

public sealed partial class AvaloniaCodeMilestone4DesignTests
{
    [Fact]
    public void ViewKeepsTheExistingDataContextWorkspaceAndSessionContract()
    {
        var view = ReadCodeXaml();
        var root = view.Root!;
        var workspace = Named(view, "WorkspacePicker");

        Assert.Equal("vm:AedaCodeModuleViewModel", Attribute(root, "DataType"));
        Assert.Equal("{Binding Workspaces}", Attribute(workspace, "ItemsSource"));
        Assert.Equal("{Binding SelectedWorkspace, Mode=TwoWay}", Attribute(workspace, "SelectedItem"));
        Assert.Equal("OnWorkspaceSelectionChanged", Attribute(workspace, "SelectionChanged"));
        AssertButtonCommand(view, "Refresh AEDA Code", "{Binding RefreshCommand}");
        AssertButtonCommand(view, "Start supervised Code session", "{Binding StartSessionCommand}");
        Assert.Contains("{Binding WorkspaceSummary}", view.ToString());
        Assert.Contains("{Binding SessionStatusText}", view.ToString());
    }

    [Fact]
    public void SafeStatusMessageIsTheOnlyPoliteLiveRegion()
    {
        var view = ReadCodeXaml();
        var liveRegions = view.Descendants()
            .Where(element => Attribute(element, "LiveSetting") == "Polite")
            .ToArray();

        var liveRegion = Assert.Single(liveRegions);
        Assert.Equal("{Binding SafeStatusMessage}", Attribute(liveRegion, "Text"));
        Assert.Equal("Code status", Attribute(liveRegion, "Name"));
    }

    [Fact]
    public void WorkflowKeepsAllFourReachableTabsInOrder()
    {
        var tabs = ReadCodeXaml().Descendants()
            .Where(element => element.Name.LocalName == "TabItem")
            .ToArray();

        Assert.Equal(
            ["Proposal", "Review and apply", "Validation", "Timeline"],
            tabs.Select(tab => Attribute(tab, "Header")));
        Assert.All(tabs, tab => Assert.NotEqual("False", Attribute(tab, "IsEnabled")));
    }

    [Fact]
    public void ProposalFormKeepsRealLimitsCommandsProgressAndFailure()
    {
        var view = ReadCodeXaml();
        var source = view.ToString();
        var title = AutomationNamed(view, "Proposal title");
        var request = Named(view, "ProposalRequestBox");

        Assert.Equal("{Binding ProposalTitle, Mode=TwoWay}", Attribute(title, "Text"));
        Assert.Equal("120", Attribute(title, "MaxLength"));
        Assert.Equal("{Binding ProposalRequest, Mode=TwoWay}", Attribute(request, "Text"));
        Assert.Equal("4000", Attribute(request, "MaxLength"));
        Assert.Contains("CreateProposalCommand", source);
        Assert.Contains("CancelProposalCreationCommand", source);
        Assert.Contains("ProposalCreationProgressText", source);
        Assert.Contains("ProposalCreationFailureText", source);
        Assert.Contains("HasProposalCreationFailure", source);
        Assert.DoesNotContain("Retry proposal", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RetryProposalCommand", source);
    }

    [Fact]
    public void ContextAndTargetPickersUseOnlyExistingBoundedCommandsAndItems()
    {
        var view = ReadCodeXaml();
        var source = view.ToString();

        Assert.Contains("ContextFileSearchQuery, Mode=TwoWay", source);
        Assert.Contains("SearchContextFilesCommand", source);
        Assert.Contains("ItemsSource=\"{Binding ContextFileCandidates}\"", source);
        AssertCommandParameter(view, "Add context file", "AddContextFileCommand");
        Assert.Contains("ItemsSource=\"{Binding SelectedContextFiles}\"", source);
        AssertCommandParameter(view, "Remove context file", "RemoveContextFileCommand");
        Assert.Contains("ClearSelectedContextCommand", source);
        Assert.Contains("ItemsSource=\"{Binding TargetSnippetCandidates}\"", source);
        AssertCommandParameter(view, "Select target method or member", "SelectTargetSnippetCommand");
        Assert.Contains("ClearTargetSnippetCommand", source);
        Assert.Contains("SelectedContextSummaryText", source);
        Assert.Contains("SelectedContextWarningText", source);
        Assert.DoesNotContain("token", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("embedding", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TreeView", source);
    }

    [Fact]
    public void ProposalHistoryKeepsClickActivationAndHasNoInventedManagementControls()
    {
        var view = ReadCodeXaml();
        var history = Named(view, "ProposalHistoryRegion").ToString();

        Assert.Contains("ItemsSource=\"{Binding Proposals}\"", history);
        Assert.Contains("Click=\"OnProposalClick\"", history);
        Assert.Contains("AutomationProperties.Name=\"{Binding Title}\"", history);
        Assert.Contains("MinHeight\" Value=\"64", view.ToString());
        Assert.DoesNotContain("Search", history);
        Assert.DoesNotContain("Filter", history);
        Assert.DoesNotContain("Delete", history);
        Assert.DoesNotContain("Archive", history);
    }

    [Fact]
    public void ProposalReviewUsesConfirmedFieldsAndOnePlainBoundedDiff()
    {
        var view = ReadCodeXaml();
        var detail = Named(view, "ProposalDetailPane").ToString();
        var diff = AutomationNamed(view, "Bounded unified diff preview");

        Assert.Contains("SelectedProposalMetadataText", detail);
        Assert.Contains("SelectedProposalSummaryText", detail);
        Assert.Contains("RiskSummary", detail);
        Assert.Contains("HashStatusText", detail);
        Assert.Contains("SourceSummaryText", detail);
        Assert.Contains("ProposalFiles", detail);
        Assert.Contains("RelativePath", detail);
        Assert.Equal("{Binding UnifiedDiffPreview, Mode=OneWay}", Attribute(diff, "Text"));
        Assert.Equal("True", Attribute(diff, "IsReadOnly"));
        Assert.Equal("NoWrap", Attribute(diff, "TextWrapping"));
        Assert.Equal("360", Attribute(diff, "MaxHeight"));
        Assert.Equal("Auto", Attribute(diff, "HorizontalScrollBarVisibility"));
        Assert.Equal("Auto", Attribute(diff, "VerticalScrollBarVisibility"));
        Assert.DoesNotContain("Copy", detail);
    }

    [Fact]
    public void ReviewAndApplyKeepsDryRunApprovalApplyAndRollbackSeparate()
    {
        var source = ReadCodeXaml().ToString();

        Assert.Contains("ReviewGateOrderText", source);
        Assert.Contains("DryRunSelectedProposalCommand", source);
        Assert.Contains("RequestApplyApprovalCommand", source);
        Assert.Contains("AllowApplyOnceCommand", source);
        Assert.Contains("DenyApplyApprovalCommand", source);
        Assert.Contains("ApplyApprovedProposalCommand", source);
        Assert.Contains("ItemsSource=\"{Binding ApplyResults}\"", source);
        Assert.Contains("Click=\"OnApplyResultClick\"", source);
        Assert.Contains("IsVisible=\"{Binding CanShowRollback}\"", source);
        Assert.Contains("RollbackSelectedApplyResultCommand", source);
        Assert.DoesNotContain("Approve &amp; apply", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Approve & apply", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Reject proposal", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidationKeepsTemplateAndAllFiveExplicitCommands()
    {
        var view = ReadCodeXaml();
        var source = view.ToString();
        var template = AutomationNamed(view, "Validation template");
        var output = AutomationNamed(view, "Sanitized validation output");

        Assert.Equal("{Binding ValidationTemplates}", Attribute(template, "ItemsSource"));
        Assert.Equal("{Binding SelectedValidationTemplate, Mode=TwoWay}", Attribute(template, "SelectedItem"));
        Assert.Contains("CreateValidationRunCommand", source);
        Assert.Contains("RequestValidationApprovalCommand", source);
        Assert.Contains("AllowValidationOnceCommand", source);
        Assert.Contains("DenyValidationApprovalCommand", source);
        Assert.Contains("RunApprovedValidationCommand", source);
        Assert.Equal("{Binding ValidationOutputPreview, Mode=OneWay}", Attribute(output, "Text"));
        Assert.Equal("True", Attribute(output, "IsReadOnly"));
        Assert.Equal("NoWrap", Attribute(output, "TextWrapping"));
        Assert.DoesNotContain("Terminal", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("automatic validation", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimelineRemainsScopedToTheSelectedCodeTask()
    {
        var view = ReadCodeXaml();
        var source = view.ToString();

        Assert.Contains("ItemsSource=\"{Binding RecentCodeTasks}\"", source);
        Assert.Contains("Click=\"OnTaskClick\"", source);
        Assert.Contains("AutomationProperties.Name=\"{Binding Title}\"", Named(view, "TaskListPane").ToString());
        Assert.Contains("ItemsSource=\"{Binding CodeTimelineGroups}\"", source);
        Assert.Contains("Events for the selected Code task.", source);
        Assert.DoesNotContain("workspace-wide", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponsiveMeasurementsMatchTheApprovedThreeLayouts()
    {
        var wide = CodeView.ResolveLayout(compact: false, medium: false);
        var medium = CodeView.ResolveLayout(compact: false, medium: true);
        var compact = CodeView.ResolveLayout(compact: true, medium: false);

        Assert.Equal(32, wide.PagePadding);
        Assert.Equal(24, wide.HeaderPadding);
        Assert.Equal(400, wide.ProposalColumnWidth);
        Assert.Equal(340, wide.TaskColumnWidth);
        Assert.Equal(24, wide.StageGap);
        Assert.Equal(28, wide.HeadingSize);
        Assert.Equal(24, medium.PagePadding);
        Assert.Equal(320, medium.ProposalColumnWidth);
        Assert.Equal(280, medium.TaskColumnWidth);
        Assert.Equal(16, compact.PagePadding);
        Assert.Equal(0, compact.ProposalColumnWidth);
        Assert.Equal(0, compact.TaskColumnWidth);
    }

    [Fact]
    public void FixedControlMeasurementsRemainMinimumsForTextScaling()
    {
        var view = ReadCodeXaml();
        var controls = new[]
        {
            (Named(view, "WorkspacePicker"), "36"),
            (AutomationNamed(view, "Refresh AEDA Code"), "36"),
            (AutomationNamed(view, "Start supervised Code session"), "36"),
            (AutomationNamed(view, "Proposal title"), "38"),
            (AutomationNamed(view, "Context file search"), "34"),
            (AutomationNamed(view, "Search context files"), "34")
        };

        Assert.All(controls, control =>
        {
            Assert.Equal(control.Item2, Attribute(control.Item1, "MinHeight"));
            Assert.Null(Attribute(control.Item1, "Height"));
        });
    }

    [Fact]
    public void CompactTransitionsPreserveSelectionAndDoNotReload()
    {
        var source = ReadAvaloniaSource("Views", "Code", "CodeView.axaml.cs");
        var proposalBack = MethodSlice(source, "OnBackToProposalsClick", "OnBackToCodeTasksClick");
        var taskBack = MethodSlice(source, "OnBackToCodeTasksClick", "UpdateCompactPresentation");

        Assert.Contains("_compactProposalDetailActive = false", proposalBack);
        Assert.DoesNotContain("SelectProposalAsync", proposalBack);
        Assert.DoesNotContain("SelectedProposal =", proposalBack);
        Assert.Contains("_compactTimelineDetailActive = false", taskBack);
        Assert.DoesNotContain("SelectTaskAsync", taskBack);
        Assert.DoesNotContain("SelectedTask =", taskBack);
        Assert.Contains("SelectProposalAsync(proposal)", source);
        Assert.Contains("SelectTaskAsync(task)", source);
        Assert.Contains("ProposalReviewHeading.Focus()", source);
        Assert.Contains("TimelineHeading.Focus()", source);
    }

    [Fact]
    public void FocusAndShellResponsiveBridgeUseTheExistingViewLocalContract()
    {
        var code = ReadAvaloniaSource("Views", "Code", "CodeView.axaml.cs");
        var shell = ReadAvaloniaSource("MainWindow.axaml.cs");

        Assert.Contains("WorkflowTabs.SelectedIndex = 0", code);
        Assert.Contains("_compactProposalDetailActive = false", MethodSlice(code, "FocusPrimaryAction", "ApplyResponsiveMode"));
        Assert.Contains("ProposalRequestBox.Focus()", MethodSlice(code, "FocusPrimaryAction", "ApplyResponsiveMode"));
        Assert.Contains("DashboardRoute.ApplyResponsiveMode(compact, medium)", shell);
        Assert.Contains("ChatRoute.ApplyResponsiveMode(compact, medium)", shell);
        Assert.Contains("codeRoute.ApplyResponsiveMode(compact, medium)", shell);
        Assert.DoesNotContain("new CodeNavigation", shell);
    }

    [Fact]
    public void RedesignedCodePresentationUsesOnlySemanticThemeResources()
    {
        var source = ReadAvaloniaSource("Views", "Code", "CodeView.axaml");
        var resources = DynamicResourceRegex().Matches(source)
            .Cast<Match>()
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray();
        string[] allowed =
        [
            "ContentSurfaceBrush", "CardSurfaceBrush", "ElevatedSurfaceBrush",
            "SubtleSurfaceBrush", "SurfaceAltBrush", "BorderBrush",
            "StrongBorderBrush", "PrimaryTextBrush", "SecondaryTextBrush",
            "MutedTextBrush", "AccentBrush", "AccentSoftBrush", "AccentTextBrush",
            "FocusRingBrush", "SuccessBrush", "WarningBrush", "ErrorBrush"
        ];

        Assert.All(resources, resource => Assert.Contains(resource, allowed));
        Assert.DoesNotContain("AedaSurfaceBrush", source);
        Assert.DoesNotContain("AedaBorderBrush", source);
        Assert.DoesNotMatch(HexColorRegex(), source);
    }

    [GeneratedRegex(@"\{DynamicResource\s+([A-Za-z0-9]+)\}")]
    private static partial Regex DynamicResourceRegex();

    [GeneratedRegex(@"#[0-9A-Fa-f]{3,8}\b")]
    private static partial Regex HexColorRegex();

    private static XDocument ReadCodeXaml() =>
        XDocument.Parse(ReadAvaloniaSource("Views", "Code", "CodeView.axaml"));

    private static XElement Named(XDocument document, string name) =>
        Assert.Single(document.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == name));

    private static XElement AutomationNamed(XDocument document, string name) =>
        Assert.Single(document.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name" && attribute.Value == name));

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == localName ||
            attribute.Name.LocalName.EndsWith($".{localName}", StringComparison.Ordinal))?.Value;

    private static void AssertButtonCommand(XDocument document, string accessibleName, string command)
    {
        var button = AutomationNamed(document, accessibleName);
        Assert.Equal("Button", button.Name.LocalName);
        Assert.Equal(command, Attribute(button, "Command"));
    }

    private static void AssertCommandParameter(XDocument document, string accessibleName, string commandName)
    {
        var button = AutomationNamed(document, accessibleName);
        Assert.Contains(commandName, Attribute(button, "Command") ?? string.Empty);
        Assert.Equal("{Binding}", Attribute(button, "CommandParameter"));
    }

    private static string MethodSlice(string source, string methodName, string nextMethodName)
    {
        var start = source.IndexOf(methodName, StringComparison.Ordinal);
        var end = source.IndexOf(nextMethodName, start + methodName.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not slice {methodName}.");
        return source[start..end];
    }

    private static string ReadAvaloniaSource(params string[] relativePath)
    {
        var repositoryRoot = GetRepositoryRoot();
        var path = Path.Combine([repositoryRoot, "PersonalAI.Desktop.Avalonia", .. relativePath]);
        Assert.True(File.Exists(path), $"Avalonia source not found at {path}");
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot([CallerFilePath] string testFilePath = "")
    {
        var directory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(directory, "..", "..", ".."));
    }
}
