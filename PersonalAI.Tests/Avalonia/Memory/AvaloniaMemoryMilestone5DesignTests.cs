using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PersonalAI.Desktop.Avalonia.Views.Memory;

namespace PersonalAI.Tests.Avalonia.Memory;

public sealed partial class AvaloniaMemoryMilestone5DesignTests
{
    [Fact]
    public void ViewKeepsTheCompiledMemoryDataContextAndOverviewBindings()
    {
        var view = ReadMemoryXaml();
        var source = view.ToString();

        Assert.Equal("vm:AedaMemoryModuleViewModel", Attribute(view.Root!, "DataType"));
        Assert.Contains("{Binding PrivacyStatusText}", source);
        Assert.Contains("{Binding TotalMemoryCountText}", source);
        Assert.Contains("{Binding IndexedKnowledgeText}", source);
        Assert.Contains("{Binding RetrievalStatusText}", source);
        Assert.DoesNotContain("Total memories", source);
        Assert.DoesNotContain("Complete documents", source);
        Assert.DoesNotContain("automatic-memory toggle", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeStatusMessageIsTheOnlyUnlabeledPoliteLiveRegion()
    {
        var liveRegions = ReadMemoryXaml().Descendants()
            .Where(element => Attribute(element, "LiveSetting") == "Polite")
            .ToArray();

        var liveRegion = Assert.Single(liveRegions);
        Assert.Equal("{Binding SafeStatusMessage}", Attribute(liveRegion, "Text"));
        Assert.Null(Attribute(liveRegion, "Name"));
        Assert.Null(Attribute(liveRegion, "LabeledBy"));
        Assert.Null(Attribute(liveRegion, "HelpText"));
    }

    [Fact]
    public void SourceNavigationIsExactCountFreeAndViewLocal()
    {
        var view = ReadMemoryXaml();
        var navigation = Named(view, "SourceNavigation");
        var labels = navigation.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => Attribute(element, "Text") ?? string.Empty)
            .ToArray();
        var code = ReadAvaloniaSource("Views", "Memory", "MemoryView.axaml.cs");

        Assert.Equal(["Recent memories", "Task outcomes", "Indexed knowledge"], labels);
        Assert.DoesNotContain("Count", navigation.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("private MemorySource _source", code);
        Assert.DoesNotContain("viewModel.Source", code);
    }

    [Fact]
    public void SearchResultsCoexistWithTheSelectedDashboardSource()
    {
        var view = ReadMemoryXaml();
        var workspace = Named(view, "RecordWorkspace").ToString();
        var code = ReadAvaloniaSource("Views", "Memory", "MemoryView.axaml.cs");

        Assert.Contains("Text=\"{Binding SearchText}\"", workspace);
        Assert.Contains("Command=\"{Binding SearchMemoriesCommand}\"", workspace);
        Assert.Contains("ItemsSource=\"{Binding SearchResults}\"", workspace);
        Assert.Contains("ItemsSource=\"{Binding Dashboard.RecentMemories}\"", workspace);
        Assert.Contains("ItemsSource=\"{Binding Dashboard.RecentTaskOutcomes}\"", workspace);
        Assert.Contains("No search results to show.", workspace);
        Assert.DoesNotContain("SearchText.Trim", code);
        Assert.DoesNotContain("searchExecuted", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HasSearched", code);
    }

    [Fact]
    public void EveryMemoryCollectionUsesTheRealBoundedRowActions()
    {
        var view = ReadMemoryXaml();
        var template = Resource(view, "MemorySummaryTemplate");
        var templateSource = template.ToString();

        Assert.Contains("Click=\"OnOpenMemoryClick\"", templateSource);
        Assert.Contains("Click=\"OnArchiveMemoryClick\"", templateSource);
        Assert.Contains("Click=\"OnDeleteMemoryClick\"", templateSource);
        Assert.Contains("ConverterParameter=Open memory", templateSource);
        Assert.Contains("ConverterParameter=Archive memory", templateSource);
        Assert.Contains("ConverterParameter=Delete memory", templateSource);
        Assert.Equal(3, view.Descendants().Count(element =>
            Attribute(element, "ItemTemplate") == "{StaticResource MemorySummaryTemplate}"));
        Assert.DoesNotContain("Confirm", templateSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Undo", templateSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Restore", templateSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Edit", templateSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IndexedKnowledgeIsReadOnlyAndUsesOnlySafeSummaryFields()
    {
        var view = ReadMemoryXaml();
        var surface = Named(view, "IndexedKnowledgeSurface").ToString();
        var template = Resource(view, "DocumentSummaryTemplate").ToString();

        Assert.Contains("Dashboard.RecentDocuments", surface);
        Assert.Contains("Title", template);
        Assert.Contains("RelativePath", template);
        Assert.Contains("SourceType", template);
        Assert.Contains("State", template);
        Assert.Contains("ChunkCount", template);
        Assert.Contains("UpdatedAtUtc", template);
        Assert.DoesNotContain("Button", template);
        Assert.DoesNotContain("Click=", template);
        Assert.DoesNotContain("TraceId", template);
        Assert.DoesNotContain("WorkspaceId", template);
    }

    [Fact]
    public void SelectedDetailUsesConfirmedSafeMemoryFieldsOnly()
    {
        var detail = Named(ReadMemoryXaml(), "SelectedDetailSurface");
        var source = detail.ToString();

        Assert.Equal("Selected memory detail", AutomationAttribute(detail, "Name"));
        Assert.Contains("{Binding HasSelectedMemory}", source);
        Assert.Contains("{Binding HasNoSelectedMemory}", source);
        Assert.Contains("SelectedMemory.Text", source);
        Assert.Contains("SelectedMemory.Kind.Label", source);
        Assert.Contains("SelectedMemory.Visibility", source);
        Assert.Contains("SelectedMemory.SensitivityStatus", source);
        Assert.Contains("SelectedMemory.Confidence", source);
        Assert.Contains("SelectedMemory.CreatedAtUtc", source);
        Assert.Contains("SelectedMemory.UpdatedAtUtc", source);
        Assert.Contains("SelectedMemory.Source.DisplayName", source);
        Assert.Contains("SelectedMemory.Source.RelativePath", source);
        Assert.DoesNotContain("SelectedMemory.Id", source);
        Assert.DoesNotContain("TraceId", source);
        Assert.DoesNotContain("Rank", source);
    }

    [Fact]
    public void AddMemoryKeepsTwoEditableRequiredFieldsAndTheRealCommand()
    {
        var source = Named(ReadMemoryXaml(), "AddMemorySurface").ToString();

        Assert.Contains("Text=\"{Binding NewMemoryText}\"", source);
        Assert.Contains("Text=\"{Binding NewMemorySourceReason}\"", source);
        Assert.Contains("Command=\"{Binding CreateExplicitMemoryCommand}\"", source);
        Assert.DoesNotContain("MaxLength", source);
        Assert.DoesNotContain("CheckBox", source);
        Assert.DoesNotContain("IsReadOnly=\"True\"", source);
        Assert.DoesNotContain("Cancel", source);
        Assert.DoesNotContain("Retry", source);
    }

    [Fact]
    public void RetrievalPreviewIsReadOnlyAndUsesTheConfirmedFields()
    {
        var view = ReadMemoryXaml();
        var surface = Named(view, "RetrievalSurface").ToString();
        var template = Resource(view, "RetrievalPreviewTemplate").ToString();

        Assert.Contains("Text=\"{Binding RetrievalQuery}\"", surface);
        Assert.Contains("Command=\"{Binding PreviewRetrievalCommand}\"", surface);
        Assert.Contains("ItemsSource=\"{Binding RetrievalPreview}\"", surface);
        Assert.Contains("SourceLabel", template);
        Assert.Contains("PreviewText", template);
        Assert.Contains("MatchType", template);
        Assert.Contains("Score", template);
        Assert.DoesNotContain("Button", template);
        Assert.DoesNotContain("Click=", template);
        Assert.DoesNotContain("Rank", template);
        Assert.DoesNotContain("SelectedMemory", template);
    }

    [Fact]
    public void ResponsiveMeasurementsMatchTheApprovedLayouts()
    {
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", ReadAvaloniaSource("Views", "Memory", "MemoryView.axaml"));

        var wide = MemoryView.ResolveLayout(compact: false, medium: false);
        var medium = MemoryView.ResolveLayout(compact: false, medium: true);
        var compact = MemoryView.ResolveLayout(compact: true, medium: false);

        Assert.Equal((32, 24, 200, 380, 320, 24, 28), wide);
        Assert.Equal(24, medium.PagePadding);
        Assert.Equal(20, medium.TrustPadding);
        Assert.Equal(320, medium.RecordListWidth);
        Assert.Equal(20, medium.MajorGap);
        Assert.Equal(16, compact.PagePadding);
        Assert.Equal(16, compact.TrustPadding);
        Assert.Equal(16, compact.MajorGap);
    }

    [Fact]
    public void CompactUsesOneLocalPaneAndBackNeverReloadsOrClearsState()
    {
        var source = ReadAvaloniaSource("Views", "Memory", "MemoryView.axaml.cs");
        var back = MethodSlice(source, "OnCompactBackClick", "SelectSource");
        var open = MethodSlice(source, "OnOpenMemoryClick", "OnArchiveMemoryClick");

        Assert.Contains("CompactPane.Overview", source);
        Assert.Contains("CompactPane.MemoryList", source);
        Assert.Contains("CompactPane.SelectedDetail", source);
        Assert.Contains("CompactPane.IndexedKnowledge", source);
        Assert.Contains("CompactPane.AddMemory", source);
        Assert.Contains("CompactPane.Retrieval", source);
        Assert.Contains("await viewModel.OpenMemoryDetailAsync(summary)", open);
        Assert.Contains("viewModel.SelectedMemory?.Id != summary.Id", open);
        Assert.DoesNotContain("OpenMemoryDetailAsync", back);
        Assert.DoesNotContain("InitializeAsync", back);
        Assert.DoesNotContain("SelectedMemory =", back);
        Assert.DoesNotContain("SearchMemories", back);
        Assert.DoesNotContain("PreviewRetrieval", back);
    }

    [Fact]
    public void FocusAndShellBridgeReuseTheExistingContract()
    {
        var memory = ReadAvaloniaSource("Views", "Memory", "MemoryView.axaml.cs");
        var shell = ReadAvaloniaSource("MainWindow.axaml.cs");
        var focus = MethodSlice(memory, "FocusPrimaryAction", "ApplyResponsiveMode");

        Assert.Contains("_compactPane = CompactPane.MemoryList", focus);
        Assert.Contains("MemorySearchTextBox.Focus()", focus);
        Assert.Contains("SelectedMemoryDetailHeading.Focus()", memory);
        Assert.Contains("_lastOpenButton?.Focus()", memory);
        Assert.Contains("_lastOverviewButton?.Focus()", memory);
        Assert.Contains("DashboardRoute.ApplyResponsiveMode(compact, medium)", shell);
        Assert.Contains("ChatRoute.ApplyResponsiveMode(compact, medium)", shell);
        Assert.Contains("codeRoute.ApplyResponsiveMode(compact, medium)", shell);
        Assert.Contains("memoryRoute.ApplyResponsiveMode(compact, medium)", shell);
    }

    [Fact]
    public void RepeatedResponsiveCallsDoNotInvokeMemoryBehavior()
    {
        var source = ReadAvaloniaSource("Views", "Memory", "MemoryView.axaml.cs");
        var responsive = MethodSlice(source, "ApplyResponsiveMode", "internal static (");

        Assert.DoesNotContain("InitializeAsync", responsive);
        Assert.DoesNotContain("SearchMemories", responsive);
        Assert.DoesNotContain("CreateExplicitMemory", responsive);
        Assert.DoesNotContain("OpenMemoryDetail", responsive);
        Assert.DoesNotContain("ArchiveMemory", responsive);
        Assert.DoesNotContain("DeleteMemory", responsive);
        Assert.DoesNotContain("PreviewRetrieval", responsive);
        Assert.DoesNotContain(".Focus()", responsive);
    }

    [Fact]
    public void AccessibleNamesAreBoundedAndSelectedRowsFollowTheLoadedId()
    {
        var converter = MemoryPresentationConverters.BoundedName;
        var result = Assert.IsType<string>(converter.Convert(
            new string('x', 100),
            typeof(string),
            "Open memory",
            CultureInfo.InvariantCulture));

        Assert.Equal("Open memory: " + new string('x', 47) + "…", result);
        Assert.True(Assert.IsType<bool>(MemoryPresentationConverters.IsSelected.Convert(
            ["same", "same"],
            typeof(bool),
            null,
            CultureInfo.InvariantCulture)));
        Assert.False(Assert.IsType<bool>(MemoryPresentationConverters.IsSelected.Convert(
            ["row", "selected"],
            typeof(bool),
            null,
            CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void RedesignedMemoryUsesOnlyEstablishedSemanticResources()
    {
        var source = ReadAvaloniaSource("Views", "Memory", "MemoryView.axaml");
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

    private static XDocument ReadMemoryXaml() =>
        XDocument.Parse(ReadAvaloniaSource("Views", "Memory", "MemoryView.axaml"));

    private static XElement Named(XDocument document, string name) =>
        Assert.Single(document.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == name));

    private static XElement Resource(XDocument document, string key) =>
        Assert.Single(document.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key" && attribute.Value == key));

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
