using System.Xml.Linq;

namespace PersonalAI.Tests.Ui;

public sealed class AedaThemeSourceTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void EveryThemePaletteDefinesRuntimeAndTitleBarResources()
    {
        foreach (var id in new[] { "SystemMica", "Graphite", "MineralStone", "SharpAlmond" })
        {
            var document = XDocument.Load(Path.Combine(
                RepositoryRoot,
                "PersonalAI.Desktop.WinUI",
                "Themes",
                "Palettes",
                $"{id}.xaml"));
            var keys = document.Descendants()
                .Select(element => element.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.LocalName == "Key")?.Value)
                .Where(value => value is not null)
                .ToHashSet();

            Assert.Contains("PersonalAiPageBackgroundBrush", keys);
            Assert.Contains("PersonalAiSidebarBackgroundBrush", keys);
            Assert.Contains("AedaDashboardHeroBrush", keys);
            Assert.Contains("AedaTitleBarBackgroundColor", keys);
            Assert.Contains("AedaWindowBorderColor", keys);
            Assert.Contains("AedaFocusStroke", keys);
            Assert.Contains("AedaAssistIdleBrush", keys);
        }
    }

    [Fact]
    public void NativeWindowsApplyThemeChromeAndCommittedApplicationIcon()
    {
        var project = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "PersonalAI.Desktop.WinUI.csproj"));
        var main = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Views", "MainWindow.xaml.cs"));
        var assist = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Views", "AssistPillWindow.xaml.cs"));
        var chrome = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Services", "AedaWindowChrome.cs"));
        var icon = Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Assets", "AedaAppIcon.ico");

        Assert.True(File.Exists(icon));
        Assert.NotEqual(0, new FileInfo(icon).Length);
        Assert.Contains("<ApplicationIcon>Assets\\AedaAppIcon.ico</ApplicationIcon>", project);
        Assert.Contains("Assets\\AedaAppIcon.ico\" CopyToOutputDirectory=\"PreserveNewest\"", project);
        Assert.Contains("AedaWindowChrome.Apply", main);
        Assert.Contains("AedaWindowChrome.Apply", assist);
        Assert.Contains("BorderColor = 34", chrome);
        Assert.Contains("CaptionColor = 35", chrome);
        Assert.Contains("TextColor = 36", chrome);
        Assert.Contains("appWindow.SetIcon(iconPath)", chrome);
    }

    [Fact]
    public void PrimaryModulePagesDoNotRenderCapabilityParadesOrRawProviderCodes()
    {
        var shell = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Views", "MainWindow.xaml"));
        var research = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Controls", "AedaResearchWorkspaceView.xaml"));
        var memory = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Controls", "AedaMemoryWorkspaceView.xaml"));

        Assert.DoesNotContain("ItemsSource=\"{Binding CapabilityBadges}\"", shell + research + memory);
        Assert.DoesNotContain("{Binding SafeStatusCode}", shell + research + memory);
        Assert.Contains("{Binding ProviderStatusLabels}", research);
    }

    [Fact]
    public void ShellAndAssistUseTheRealLogoWithoutLosingCommands()
    {
        var shell = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Views", "MainWindow.xaml"));
        var assist = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Views", "AssistPillWindow.xaml"));

        Assert.Contains("ms-appx:///Assets/AedaLogo.svg", shell);
        Assert.Contains("ms-appx:///Assets/AedaLogo.svg", assist);
        Assert.Contains("OpenDashboardCommand", shell);
        Assert.Contains("RetryCommand", assist);
        Assert.Contains("CancelCommand", assist);
        Assert.Contains("OpenInAedaCommand", assist);
    }

    [Fact]
    public void DashboardHasFourNativeThemeCompositionsAndNoLegacyShellTemplate()
    {
        var dashboard = XDocument.Load(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Controls", "AedaDashboardView.xaml"));
        var shell = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Views", "MainWindow.xaml"));

        foreach (var flag in new[]
                 {
                     "IsSystemMicaTheme", "IsGraphiteTheme", "IsMineralStoneTheme", "IsSharpAlmondTheme"
                 })
        {
            Assert.Contains(dashboard.Descendants(), element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Visibility" && attribute.Value.Contains(flag, StringComparison.Ordinal)));
        }

        Assert.Contains("AedaDashboardView", shell);
        Assert.DoesNotContain("AEDA Dashboard", shell);
    }

    [Fact]
    public void ReconstructedShellUsesCompactRailAndIntegratedTitleBar()
    {
        var shell = XDocument.Load(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Views", "MainWindow.xaml"));
        var shellGrid = shell.Descendants().Single(element =>
            element.Name.LocalName == "Grid" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ShellGrid"));

        Assert.Contains(shellGrid.Descendants(), element =>
            element.Name.LocalName == "ColumnDefinition" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Width" && attribute.Value == "72"));
        Assert.Contains(shell.Descendants(), element =>
            element.Name.LocalName == "Grid" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "TitleBarDragRegion"));
    }
}
