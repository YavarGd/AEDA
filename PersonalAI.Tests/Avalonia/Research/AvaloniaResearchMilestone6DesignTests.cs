using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PersonalAI.Desktop.Avalonia.Views.Research;

namespace PersonalAI.Tests.Avalonia.Research;

public sealed partial class AvaloniaResearchMilestone6DesignTests
{
    [Fact]
    public void ViewKeepsTheCompiledResearchContextAndDirectCommands()
    {
        var view = ReadResearchXaml();
        var source = view.ToString();

        Assert.Equal("vm:AedaResearchModuleViewModel", Attribute(view.Root!, "DataType"));
        Assert.Contains("Text=\"{Binding VerificationText}\"", source);
        Assert.Contains("Command=\"{Binding VerifyWithLocalEvidenceCommand}\"", source);
        Assert.Contains("Command=\"{Binding ExtractClaimsCommand}\"", source);
        Assert.DoesNotContain("MaxLength", Named(view, "VerificationTextBox").ToString());
        Assert.DoesNotContain("SelectedClaim", source);
    }

    [Fact]
    public void SafeStatusMessageIsTheOnlyUnlabeledPoliteLiveRegion()
    {
        var view = ReadResearchXaml();
        var liveRegions = view.Descendants()
            .Where(element => Attribute(element, "LiveSetting") == "Polite")
            .ToArray();

        var liveRegion = Assert.Single(liveRegions);
        Assert.Equal("{Binding SafeStatusMessage}", Attribute(liveRegion, "Text"));
        Assert.Null(AutomationAttribute(liveRegion, "Name"));
        Assert.Null(AutomationAttribute(liveRegion, "LabeledBy"));
        Assert.Null(AutomationAttribute(liveRegion, "HelpText"));
        Assert.Contains(liveRegion, Named(view, "StatusSurface").Descendants());
    }

    [Fact]
    public void ProviderAndPrivacyStatusRemainVerbatimAndNonInteractive()
    {
        var summary = Named(ReadResearchXaml(), "CapabilitySummary").ToString();

        Assert.Contains("{Binding PrivacyStatusText}", ReadResearchXaml().ToString());
        Assert.Contains("ItemsSource=\"{Binding ProviderStatusLabels}\"", summary);
        Assert.DoesNotContain("CheckBox", summary);
        Assert.DoesNotContain("ToggleSwitch", summary);
        Assert.DoesNotContain("Command=", summary);
        Assert.DoesNotContain("✓", summary);
    }

    [Fact]
    public void ExtractedClaimsAreAReadOnlyOptionalPreview()
    {
        var view = ReadResearchXaml();
        var surface = Named(view, "ExtractedClaimsSurface").ToString();
        var template = Resource(view, "ExtractedClaimTemplate").ToString();

        Assert.Contains("ItemsSource=\"{Binding ExtractedClaims}\"", surface);
        Assert.Contains("Text=\"{Binding Text}\"", template);
        Assert.Contains("Text=\"{Binding Kind}\"", template);
        Assert.Contains("ResearchPresentationConverters.BoundedName", template);
        Assert.Contains("Extracting first is optional", surface);
        Assert.DoesNotContain("Button", template);
        Assert.DoesNotContain("Command=", template);
    }

    [Fact]
    public void RecentReportsUseTheProcessLocalCollectionAndExactReportFields()
    {
        var view = ReadResearchXaml();
        var surface = Named(view, "RecentReportsSurface").ToString();
        var template = Resource(view, "ReportSummaryTemplate").ToString();

        Assert.Contains("ItemsSource=\"{Binding RecentReports}\"", surface);
        Assert.Contains("process-local", surface, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Text=\"{Binding Question.Text}\"", template);
        Assert.Contains("Text=\"{Binding OverallVerdict}\"", template);
        Assert.Contains("Text=\"{Binding Status}\"", template);
        Assert.Contains("Binding Claims, Converter={x:Static research:ResearchPresentationConverters.Count}", template);
        Assert.Contains("Binding Evidence, Converter={x:Static research:ResearchPresentationConverters.Count}", template);
        Assert.Contains("ContentPresenter#PART_ContentPresenter", view.ToString());
        Assert.Contains("Click=\"OnReportClick\"", template);
        Assert.Contains("ResearchPresentationConverters.BoundedName", template);
        Assert.DoesNotContain("Delete", template, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectedReportIsTheAuthoritativeOrderedDetailSurface()
    {
        var surface = Named(ReadResearchXaml(), "SelectedReportSurface");
        var source = surface.ToString();
        var orderedBindings = new[]
        {
            "SelectedReport.Question.Text", "SelectedReport.Claims", "Findings",
            "EvidenceItems", "Citations", "UnresolvedGaps"
        };

        Assert.Equal("Selected verification report", AutomationAttribute(Named(ReadResearchXaml(), "SelectedReportSurface"), "Name"));
        Assert.Contains("SelectedReport.OverallVerdict", source);
        Assert.Contains("SelectedReport.Confidence", source);
        Assert.Contains("SelectedReport.Status", source);
        Assert.Contains("ResearchPresentationConverters.Provenance", source);
        var emptyHeading = Assert.Single(surface.Descendants(), element =>
            Attribute(element, "Text") == "No report selected");
        Assert.Equal("Wrap", Attribute(emptyHeading, "TextWrapping"));
        Assert.True(orderedBindings.Select(binding => source.IndexOf(binding, StringComparison.Ordinal)).SequenceEqual(
            orderedBindings.Select(binding => source.IndexOf(binding, StringComparison.Ordinal)).Order()));
        Assert.Contains("Choose a recent report or verify a claim", source);
    }

    [Fact]
    public void FindingsEvidenceCitationsAndGapsUseOnlyConfirmedFields()
    {
        var view = ReadResearchXaml();
        var finding = Resource(view, "FindingTemplate").ToString();
        var evidence = Resource(view, "EvidenceTemplate").ToString();
        var citation = Resource(view, "CitationTemplate").ToString();
        var gapElement = Resource(view, "GapTemplate");
        var gap = gapElement.ToString();

        Assert.Contains("Verdict", finding);
        Assert.Contains("Confidence", finding);
        Assert.Contains("SafeSummary", finding);
        Assert.DoesNotContain("Text=\"True\"", finding);
        Assert.DoesNotContain("Text=\"False\"", finding);
        Assert.DoesNotContain("Text=\"Verified\"", finding);
        Assert.DoesNotContain("Text=\"Pass\"", finding);
        Assert.DoesNotContain("Insufficient evidence", finding);
        Assert.Contains("Excerpt", evidence);
        Assert.Contains("Source.Label", evidence);
        Assert.Contains("Source.Type", evidence);
        Assert.Contains("Source.IsLocal", evidence);
        Assert.Contains("Source.RelativePath", evidence);
        Assert.Contains("Score", evidence);
        Assert.Contains("Supports", evidence);
        Assert.Contains("Contradicts", evidence);
        Assert.Contains("CitationId", citation);
        Assert.Contains("SourceLabel", citation);
        Assert.Equal("{Binding}", Attribute(gapElement.Descendants().Single(), "Text"));
        Assert.DoesNotContain("Button", finding + evidence + citation + gap);
        Assert.DoesNotContain("Rank", finding + evidence + citation + gap);
        Assert.DoesNotContain("MatchType", finding + evidence + citation + gap);
        Assert.DoesNotContain("percentage", evidence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("probability", evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponsiveMeasurementsMatchTheApprovedLayouts()
    {
        var wide = ResearchView.ResolveLayout(compact: false, medium: false);
        var medium = ResearchView.ResolveLayout(compact: false, medium: true);
        var compact = ResearchView.ResolveLayout(compact: true, medium: false);

        Assert.Equal((32, 24, 400, 320, 24, 28), wide);
        Assert.Equal((24, 20, 0, 0, 20, 24), medium);
        Assert.Equal((16, 16, 0, 0, 16, 22), compact);
    }

    [Fact]
    public void CompactNavigationIsViewLocalAndBackRestoresThePriorRegion()
    {
        var source = ReadAvaloniaSource("Views", "Research", "ResearchView.axaml.cs");
        var back = MethodSlice(source, "OnCompactBackClick", "OpenCompactPane");

        Assert.Contains("CompactPane.Overview", source);
        Assert.Contains("CompactPane.Claim", source);
        Assert.Contains("CompactPane.Extracted", source);
        Assert.Contains("CompactPane.Reports", source);
        Assert.Contains("CompactPane.Report", source);
        Assert.Contains("_lastReportButton?.Focus()", back);
        Assert.Contains("_lastOverviewButton?.Focus()", back);
        Assert.DoesNotContain("InitializeAsync", back);
        Assert.DoesNotContain("SelectReportCommand", back);
        Assert.DoesNotContain("VerifyWithLocalEvidence", back);
        Assert.DoesNotContain("ExtractClaims", back);
    }

    [Fact]
    public void ReportSelectionExecutesTheExistingCommandExactlyOnce()
    {
        var source = ReadAvaloniaSource("Views", "Research", "ResearchView.axaml.cs");
        var reportClick = MethodSlice(source, "OnReportClick", "OnCompactBackClick");

        Assert.Single(Regex.Matches(reportClick, "SelectReportCommand\\.Execute").Cast<Match>());
        Assert.Contains("viewModel.SelectedReport?.Id != report.Id", reportClick);
        Assert.DoesNotContain("SelectedReport =", reportClick);
    }

    [Fact]
    public void ResponsiveCallsReuseTheShellBooleansWithoutInvokingResearchBehavior()
    {
        var research = ReadAvaloniaSource("Views", "Research", "ResearchView.axaml.cs");
        var responsive = MethodSlice(research, "ApplyResponsiveMode", "internal static (");
        var shell = ReadAvaloniaSource("MainWindow.axaml.cs");

        Assert.Contains("researchRoute.ApplyResponsiveMode(compact, medium)", shell);
        Assert.DoesNotContain("InitializeAsync", responsive);
        Assert.DoesNotContain("SelectReport", responsive);
        Assert.DoesNotContain("VerifyWithLocalEvidence", responsive);
        Assert.DoesNotContain("ExtractClaims", responsive);
        Assert.DoesNotContain(".Focus()", responsive);
    }

    [Fact]
    public void InitializationAndPrimaryFocusKeepTheExistingContracts()
    {
        var source = ReadAvaloniaSource("Views", "Research", "ResearchView.axaml.cs");
        var focus = MethodSlice(source, "FocusPrimaryAction", "ApplyResponsiveMode");
        var attach = MethodSlice(source, "private async void OnAttachedToVisualTree", "OnOpenClaimPaneClick");

        Assert.Contains("_loaded || DataContext is not AedaResearchModuleViewModel", attach);
        Assert.Contains("_loaded = true", attach);
        Assert.Single(Regex.Matches(attach, "InitializeAsync").Cast<Match>());
        Assert.Contains("_compactPane = CompactPane.Claim", focus);
        Assert.Contains("VerificationTextBox.Focus()", focus);
        Assert.DoesNotContain("_compactPane = CompactPane.Overview", attach);
    }

    [Fact]
    public void AccessibleNamesAreBoundedAndDerivedFromSafeVisibleText()
    {
        var converter = ResearchPresentationConverters.BoundedName;
        var result = Assert.IsType<string>(converter.Convert(
            "  " + new string('x', 80) + "  ",
            typeof(string),
            "Verification report",
            CultureInfo.InvariantCulture));
        var empty = Assert.IsType<string>(converter.Convert(
            "  ", typeof(string), "Evidence", CultureInfo.InvariantCulture));

        Assert.Equal("Verification report: " + new string('x', 63) + "…", result);
        Assert.Equal("Evidence", empty);
        Assert.True(result.Length <= "Verification report: ".Length + 64);
    }

    [Theory]
    [InlineData(true, false, "Local evidence only · Remote search not used")]
    [InlineData(false, true, "Remote search used")]
    [InlineData(true, true, "Local evidence with remote search used")]
    [InlineData(false, false, "Remote search not used")]
    public void ProvenanceLabelsFollowTheLoadedReportFlags(bool localOnly, bool remoteUsed, string expected)
    {
        var result = ResearchPresentationConverters.Provenance.Convert(
            [localOnly, remoteUsed], typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, false, "Supports claim")]
    [InlineData(false, true, "Contradicts claim")]
    [InlineData(true, true, "Contradicts claim")]
    [InlineData(false, false, "Related")]
    public void EvidenceRelationUsesTheRealSupportAndContradictionFlags(bool supports, bool contradicts, string expected)
    {
        var result = ResearchPresentationConverters.EvidenceRelation.Convert(
            [supports, contradicts], typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void RedesignUsesOnlyEstablishedSemanticResources()
    {
        var source = ReadAvaloniaSource("Views", "Research", "ResearchView.axaml");
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

    private static XDocument ReadResearchXaml() =>
        XDocument.Parse(ReadAvaloniaSource("Views", "Research", "ResearchView.axaml"));

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
