using System.Runtime.CompilerServices;

namespace PersonalAI.Tests.Avalonia.Research;

public sealed class AvaloniaAedaResearchViewTests
{
    [Fact]
    public void ViewPresentsClaimsEvidenceConfidenceAndUnresolvedGapStates()
    {
        var source = ReadSource("Views", "Research", "ResearchView.axaml");

        // Claims state
        Assert.Contains("Text=\"{Binding VerificationText}\"", source);
        Assert.Contains("Command=\"{Binding ExtractClaimsCommand}\"", source);
        Assert.Contains("ItemsSource=\"{Binding ExtractedClaims}\"", source);
        Assert.Contains("IsVisible=\"{Binding HasNoClaims}\"", source);

        // Evidence and confidence state
        Assert.Contains("Command=\"{Binding VerifyWithLocalEvidenceCommand}\"", source);
        Assert.Contains("ItemsSource=\"{Binding EvidenceItems}\"", source);
        Assert.Contains("Text=\"{Binding SelectedReport.OverallVerdict}\"", source);
        Assert.Contains("Text=\"{Binding SelectedReport.Confidence}\"", source);
        Assert.Contains("Text=\"{Binding Verdict}\"", source);
        Assert.Contains("Text=\"{Binding Confidence}\"", source);

        // Unresolved gaps
        Assert.Contains("ItemsSource=\"{Binding UnresolvedGaps}\"", source);
        Assert.Contains("No unresolved gaps identified.", source);

        // Empty states
        Assert.Contains("IsVisible=\"{Binding HasNoRecentReports}\"", source);
        Assert.Contains("IsVisible=\"{Binding HasNoSelectedReport}\"", source);

        // Local-only / privacy messaging
        Assert.Contains("Text=\"{Binding PrivacyStatusText}\"", source);
        Assert.Contains("Text=\"{Binding SafeStatusMessage}\"", source);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", source);
        Assert.Contains("ItemsSource=\"{Binding ProviderStatusLabels}\"", source);

        // Accessible lists
        Assert.Contains("AutomationProperties.Name=\"Extracted claims\"", source);
        Assert.Contains("AutomationProperties.Name=\"Recent reports\"", source);
        Assert.Contains("AutomationProperties.Name=\"Evidence items\"", source);
        Assert.Contains("AutomationProperties.Name=\"Citations\"", source);
        Assert.Contains("AutomationProperties.Name=\"Unresolved gaps\"", source);
        Assert.Contains("AutomationProperties.Name=\"Research\"", source);
    }

    [Fact]
    public void ViewSupportsSelectingRecentReports()
    {
        var source = ReadSource("Views", "Research", "ResearchView.axaml");

        Assert.Contains("Click=\"OnReportClick\"", source);
    }

    [Fact]
    public void CodeBehindWiresReportSelectionToViewModelCommand()
    {
        var source = ReadSource("Views", "Research", "ResearchView.axaml.cs");

        Assert.Contains("viewModel.SelectReportCommand.Execute(report)", source);
        Assert.Contains("viewModel.InitializeAsync()", source);
    }

    [Fact]
    public void CompositionRegistersAccessibleResearchRoute()
    {
        var source = ReadSource("Composition", "AvaloniaAppComposition.cs");

        Assert.Contains("\"aeda-research\"", source);
        Assert.Contains("\"Research\"", source);
        Assert.Contains("new AedaResearchModuleViewModel(runtime.ResearchModule, runtime.ModuleRegistry)", source);
        Assert.Contains("() => new ResearchView()", source);
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
        // <repo>/PersonalAI.Tests/Avalonia/Research/<this file>
        var researchDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(researchDirectory, "..", "..", ".."));
    }
}
