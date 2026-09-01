using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PersonalAI.Core.Tasks;
using PersonalAI.Desktop.Avalonia.Views.Tasks;

namespace PersonalAI.Tests.Avalonia.Tasks;

public sealed partial class AvaloniaTaskCenterMilestone7DesignTests
{
    [Fact]
    public void ViewKeepsTheCompiledTaskCenterContextAndDirectRefreshContract()
    {
        var view = ReadTaskCenterXaml();
        var source = view.ToString();

        Assert.Equal("vm:AedaTaskCenterViewModel", Attribute(view.Root!, "DataType"));
        Assert.Equal("{Binding RefreshCommand}", Attribute(Named(view, "RefreshButton"), "Command"));
        Assert.Contains("IsVisible=\"{Binding IsRefreshing}\"", source);
        Assert.DoesNotContain("Cancel refresh", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Retry", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeStatusMessageIsTheOnlyUnlabeledPoliteLiveRegion()
    {
        var view = ReadTaskCenterXaml();
        var liveRegions = view.Descendants()
            .Where(element => Attribute(element, "LiveSetting") == "Polite")
            .ToArray();

        var liveRegion = Assert.Single(liveRegions);
        Assert.Equal("{Binding SafeStatusMessage}", Attribute(liveRegion, "Text"));
        Assert.Null(AutomationAttribute(liveRegion, "Name"));
        Assert.Null(AutomationAttribute(liveRegion, "LabeledBy"));
        Assert.Null(AutomationAttribute(liveRegion, "HelpText"));
        Assert.Contains(liveRegion, Named(view, "StatusSurface").Descendants());

        var presentation = MethodSlice(
            ReadAvaloniaSource("Views", "Tasks", "TaskCenterView.axaml.cs"),
            "private void UpdatePresentation",
            "private Control RegionFor");
        Assert.DoesNotContain("StatusSurface", presentation);
    }

    [Fact]
    public void SummaryUsesOnlyTheThreeExistingVerbatimStrings()
    {
        var summary = Named(ReadTaskCenterXaml(), "SummarySurface").ToString();

        Assert.Contains("Text=\"{Binding ActiveTaskCountText}\"", summary);
        Assert.Contains("Text=\"{Binding WaitingApprovalCountText}\"", summary);
        Assert.Contains("Text=\"{Binding RecentFailureCountText}\"", summary);
        Assert.DoesNotContain("Converter=", summary);
        Assert.DoesNotContain("StringFormat", summary);
        Assert.DoesNotContain("Button", summary);
        Assert.DoesNotContain("RecentTaskCount", ReadTaskCenterXaml().ToString());
        Assert.Contains("Browse recent task history", Named(ReadTaskCenterXaml(), "CompactOverview").ToString());
    }

    [Fact]
    public void ApprovalsRemainDirectReadOnlySafeRows()
    {
        var view = ReadTaskCenterXaml();
        var approvalSurface = Named(view, "ApprovalSurface").ToString();
        var template = Resource(view, "ApprovalRowTemplate").ToString();

        Assert.Contains("ItemsSource=\"{Binding WaitingApprovals}\"", approvalSurface);
        Assert.Contains("Text=\"{Binding Title}\"", template);
        Assert.Contains("Text=\"{Binding SafeSummary}\"", template);
        Assert.Contains("Text=\"{Binding SafeScope}\"", template);
        Assert.Contains("TaskCenterPresentationConverters.BoundedName", template);
        Assert.DoesNotContain("Button", template);
        Assert.DoesNotContain("Click=", template);
        Assert.DoesNotContain("Command=", template);
        Assert.DoesNotContain("Approve", template);
        Assert.DoesNotContain("Reject", template);
    }

    [Fact]
    public void ThreeTaskQueuesUseOneSharedKeyboardButtonTemplate()
    {
        var view = ReadTaskCenterXaml();
        var source = view.ToString();
        var template = Resource(view, "TaskRowTemplate");

        Assert.Equal("Button", template.Elements().Single().Name.LocalName);
        Assert.Equal("OnTaskClick", Attribute(template.Elements().Single(), "Click"));
        Assert.Contains("Button.taskRow:pointerover", source);
        Assert.Contains("Button:focus-visible", ReadAvaloniaSource("Styles", "Controls.axaml"));
        Assert.Equal(3, Regex.Matches(source, "ItemTemplate=\"\\{StaticResource TaskRowTemplate\\}\"").Count);
        Assert.Contains("ItemsSource=\"{Binding ActiveTasks}\"", source);
        Assert.Contains("ItemsSource=\"{Binding RecentTasks}\"", source);
        Assert.Contains("ItemsSource=\"{Binding FailedOrCancelledTasks}\"", source);
    }

    [Fact]
    public void SelectedStateUsesOnlyRowAndSelectedTaskIdentity()
    {
        var template = Resource(ReadTaskCenterXaml(), "TaskRowTemplate").ToString();

        Assert.Contains("TaskCenterPresentationConverters.IsSelectedTask", template);
        Assert.Contains("Path=\"Id\"", template);
        Assert.Contains("Path=\"DataContext.SelectedTask.Id\"", template);
        Assert.Contains("TaskCenterPresentationConverters.TaskAccessibleName", template);
        Assert.DoesNotContain("IsSelected=", template);
        Assert.DoesNotContain("viewModel.SelectedTask =", ReadAvaloniaSource("Views", "Tasks", "TaskCenterView.axaml.cs"));
    }

    [Fact]
    public void TaskSelectionCallsTheExistingAsyncPathExactlyOnce()
    {
        var source = ReadAvaloniaSource("Views", "Tasks", "TaskCenterView.axaml.cs");
        var click = MethodSlice(source, "private async void OnTaskClick", "private void OnOpenCompactPaneClick");

        Assert.Single(Regex.Matches(click, "SelectTaskAsync").Cast<Match>());
        Assert.Contains("viewModel.SelectedTask?.Id != task.Id", click);
        Assert.DoesNotContain("RefreshAsync", click);
        Assert.DoesNotContain("SelectedTask =", click);
    }

    [Fact]
    public void SelectedTaskAndTimelineBindDirectlyToRepositoryFields()
    {
        var detail = Named(ReadTaskCenterXaml(), "SelectedDetailSurface").ToString();

        Assert.Contains("SelectedTask.Title", detail);
        Assert.Contains("SelectedTask.SafeSummary", detail);
        Assert.Contains("SelectedTask.Status.Label", detail);
        Assert.Contains("SelectedTask.Module.Label", detail);
        Assert.Contains("SelectedTask.CreatedAtUtc", detail);
        Assert.Contains("SelectedTask.UpdatedAtUtc", detail);
        Assert.Contains("ItemsSource=\"{Binding TimelineGroups}\"", detail);
        Assert.Contains("Text=\"{Binding Title}\"", detail);
        Assert.Contains("ItemsSource=\"{Binding Items}\"", detail);
        Assert.DoesNotContain("SelectedTask.Id", detail);
        Assert.DoesNotContain("RouteId", detail);
        Assert.DoesNotContain("ModuleId", detail);
    }

    [Fact]
    public void TimelineEventsAndArtifactsAreDirectReadOnlyPresentations()
    {
        var view = ReadTaskCenterXaml();
        var activity = Resource(view, "TimelineItemTemplate").ToString();
        var artifact = Resource(view, "ArtifactTemplate").ToString();

        Assert.Contains("Text=\"{Binding Title}\"", activity);
        Assert.Contains("Text=\"{Binding Status.Label}\"", activity);
        Assert.Contains("Text=\"{Binding Summary}\"", activity);
        Assert.Contains("Text=\"{Binding Detail}\"", activity);
        Assert.Contains("TimestampUtc", activity);
        Assert.Contains("Text=\"{Binding Module.Label}\"", activity);
        Assert.Contains("ItemsSource=\"{Binding Links}\"", activity);
        Assert.DoesNotContain("Button", activity + artifact);
        Assert.DoesNotContain("Click=", activity + artifact);
        Assert.Contains("Text=\"{Binding Label}\"", artifact);
        Assert.Contains("Text=\"{Binding SafeSummary}\"", artifact);
        Assert.Contains("Text=\"{Binding SafeUnavailableReason}\"", artifact);
        Assert.DoesNotContain("Route", artifact);
        Assert.DoesNotContain("Uri", artifact, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PresentationDoesNotSortParseOrRemapRepositoryData()
    {
        var source = ReadAvaloniaSource("Views", "Tasks", "TaskCenterView.axaml") +
            ReadAvaloniaSource("Views", "Tasks", "TaskCenterView.axaml.cs") +
            ReadAvaloniaSource("Views", "Tasks", "TaskCenterPresentationConverters.cs");

        Assert.DoesNotContain("OrderBy", source);
        Assert.DoesNotContain("GroupBy", source);
        Assert.DoesNotContain("Reverse", source);
        Assert.DoesNotContain("Sort(", source);
        Assert.DoesNotContain("Split('|')", source);
        Assert.DoesNotContain("Queued", source);
        Assert.DoesNotContain("Interrupted", source);
        Assert.DoesNotContain("Rejected", source);
        Assert.DoesNotContain("Expired", source);
    }

    [Fact]
    public void CompactNavigationUsesExactlySixViewLocalPanesAndNoVmBackActions()
    {
        var source = ReadAvaloniaSource("Views", "Tasks", "TaskCenterView.axaml.cs");
        var enumSource = source[source.IndexOf("private enum CompactPane", StringComparison.Ordinal)..];
        var paneNames = Regex.Matches(enumSource, @"^\s{8}([A-Za-z]+),?\r?$", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        var back = MethodSlice(source, "private void OnCompactBackClick", "private void ArrangeWorkspace");

        Assert.Equal(["Overview", "Approvals", "Active", "Recent", "Failed", "SelectedTask"], paneNames);
        Assert.Contains("_originQueue", back);
        Assert.Contains("_lastTaskButton", back);
        Assert.Contains("_lastOverviewButton", back);
        Assert.DoesNotContain("RefreshAsync", back);
        Assert.DoesNotContain("SelectTaskAsync", back);
        Assert.DoesNotContain("TimelineGroups", back);
    }

    [Fact]
    public void ResponsiveMeasurementsAndShellBridgeMatchTheApprovedLayouts()
    {
        var wide = TaskCenterView.ResolveLayout(compact: false, medium: false);
        var medium = TaskCenterView.ResolveLayout(compact: false, medium: true);
        var compact = TaskCenterView.ResolveLayout(compact: true, medium: false);
        var source = ReadAvaloniaSource("Views", "Tasks", "TaskCenterView.axaml.cs");
        var responsive = MethodSlice(source, "public void ApplyResponsiveMode", "internal static (");
        var shell = ReadAvaloniaSource("MainWindow.axaml.cs");

        Assert.Equal((32, 24, 420, 500, 24, 28), wide);
        Assert.Equal((24, 20, 0, 0, 20, 24), medium);
        Assert.Equal((16, 16, 0, 0, 16, 22), compact);
        Assert.Contains("enteringCompact", responsive);
        Assert.DoesNotContain("RefreshAsync", responsive);
        Assert.DoesNotContain("SelectTaskAsync", responsive);
        Assert.DoesNotContain(".Focus()", responsive);
        Assert.Contains("taskCenterRoute.ApplyResponsiveMode(compact, medium)", shell);
    }

    [Fact]
    public void InitializationAndPrimaryFocusKeepTheExistingContracts()
    {
        var source = ReadAvaloniaSource("Views", "Tasks", "TaskCenterView.axaml.cs");
        var focus = MethodSlice(source, "public void FocusPrimaryAction", "public void ApplyResponsiveMode");
        var attach = MethodSlice(source, "private async void OnAttachedToVisualTree", "private async void OnTaskClick");

        Assert.Contains("RefreshButton.Focus()", focus);
        Assert.Contains("_loaded || DataContext is not AedaTaskCenterViewModel", attach);
        Assert.Contains("_loaded = true", attach);
        Assert.Single(Regex.Matches(attach, "RefreshAsync").Cast<Match>());
        Assert.DoesNotContain("_compactPane =", attach);
    }

    [Fact]
    public void AccessibleNamesAreWhitespaceNormalizedBoundedAndSelectionAware()
    {
        var longTitle = "  " + string.Join("   ", Enumerable.Repeat("task", 30)) + "  ";
        var bounded = Assert.IsType<string>(TaskCenterPresentationConverters.BoundedName.Convert(
            longTitle,
            typeof(string),
            "Approval request",
            CultureInfo.InvariantCulture));
        var rowId = TaskId.NewId();
        var selected = Assert.IsType<string>(TaskCenterPresentationConverters.TaskAccessibleName.Convert(
            [rowId, longTitle, rowId],
            typeof(string),
            null,
            CultureInfo.InvariantCulture));
        var empty = Assert.IsType<string>(TaskCenterPresentationConverters.TaskAccessibleName.Convert(
            [rowId, "   ", TaskId.NewId()],
            typeof(string),
            null,
            CultureInfo.InvariantCulture));

        Assert.StartsWith("Approval request: task task", bounded);
        Assert.True(bounded.Length <= "Approval request: ".Length + 64);
        Assert.StartsWith("Selected task: task task", selected);
        Assert.True(selected.Length <= "Selected task: ".Length + 64);
        Assert.Equal("Task", empty);
    }

    [Fact]
    public void TimestampConverterUsesCurrentCultureAndDeviceLocalTime()
    {
        var timestamp = new DateTimeOffset(2026, 8, 30, 14, 25, 0, TimeSpan.Zero);
        var culture = CultureInfo.GetCultureInfo("de-DE");

        var result = TaskCenterPresentationConverters.Timestamp.Convert(
            timestamp,
            typeof(string),
            null,
            culture);
        var fallback = TaskCenterPresentationConverters.Timestamp.Convert(
            "not a timestamp",
            typeof(string),
            null,
            culture);

        Assert.Equal(timestamp.ToLocalTime().ToString("g", culture), result);
        Assert.Equal(string.Empty, fallback);
    }

    [Fact]
    public void OnlyObservationalActionsArePresent()
    {
        var source = ReadAvaloniaSource("Views", "Tasks", "TaskCenterView.axaml");
        var buttonActions = ReadTaskCenterXaml().Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element => Attribute(element, "Click") ?? Attribute(element, "Command"))
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct()
            .ToArray();

        Assert.Equal(
            ["OnTaskClick", "{Binding RefreshCommand}", "OnCompactBackClick", "OnOpenCompactPaneClick"],
            buttonActions);
        Assert.DoesNotContain("New task", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content=\"Cancel\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Retry", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Approve", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Reject", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Search", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedesignUsesOnlyEstablishedSemanticResources()
    {
        var source = ReadAvaloniaSource("Views", "Tasks", "TaskCenterView.axaml");
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
        Assert.DoesNotContain("AedaBorderBrush", source);
        Assert.DoesNotMatch(HexColorRegex(), source);
    }

    [GeneratedRegex(@"\{DynamicResource\s+([A-Za-z0-9]+)\}")]
    private static partial Regex DynamicResourceRegex();

    [GeneratedRegex(@"#[0-9A-Fa-f]{3,8}\b")]
    private static partial Regex HexColorRegex();

    private static XDocument ReadTaskCenterXaml() =>
        XDocument.Parse(ReadAvaloniaSource("Views", "Tasks", "TaskCenterView.axaml"));

    private static XElement Named(XDocument document, string name) =>
        Assert.Single(document.Descendants(), element => Attribute(element, "Name") == name);

    private static XElement Resource(XDocument document, string key) =>
        Assert.Single(document.Descendants(), element => Attribute(element, "Key") == key);

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == localName ||
            attribute.Name.LocalName.EndsWith($".{localName}", StringComparison.Ordinal))?.Value;

    private static string? AutomationAttribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == $"AutomationProperties.{localName}")?.Value;

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
