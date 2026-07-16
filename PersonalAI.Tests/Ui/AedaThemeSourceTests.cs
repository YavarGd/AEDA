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
        var app = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "App.xaml.cs"));
        var chrome = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Services", "AedaWindowChrome.cs"));
        var shortcut = File.ReadAllText(Path.Combine(
            RepositoryRoot, "tools", "Install-AedaStartMenuShortcut.ps1"));
        var icon = Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Assets", "AedaAppIcon.ico");
        var mark = Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Assets", "AedaAppIcon.svg");

        Assert.True(File.Exists(icon));
        Assert.NotEqual(0, new FileInfo(icon).Length);
        Assert.True(File.Exists(mark));
        var markDocument = XDocument.Load(mark);
        Assert.Equal("0 0 256 256", markDocument.Root?.Attribute("viewBox")?.Value);
        Assert.DoesNotContain(markDocument.Descendants(), element =>
            element.Name.LocalName == "text");
        Assert.Contains("<ApplicationIcon>Assets\\AedaAppIcon.ico</ApplicationIcon>", project);
        Assert.Contains("Assets\\AedaAppIcon.svg\" CopyToOutputDirectory=\"PreserveNewest\"", project);
        Assert.Contains("Assets\\AedaAppIcon.ico\" CopyToOutputDirectory=\"PreserveNewest\"", project);
        Assert.Contains("AedaWindowChrome.Apply", main);
        Assert.Contains("AedaWindowChrome.Apply", assist);
        Assert.Contains("BorderColor = 34", chrome);
        Assert.Contains("CaptionColor = 35", chrome);
        Assert.Contains("TextColor = 36", chrome);
        Assert.Contains("appWindow.SetIcon(iconPath)", chrome);
        Assert.Contains("SetWindowIcons(windowHandle, iconPath)", chrome);
        Assert.Contains("WmSetIcon = 0x0080", chrome);
        Assert.Contains("SetCurrentProcessExplicitAppUserModelID", chrome);
        Assert.Contains("AEDA.LocalIntelligence", chrome);
        Assert.Contains("AedaWindowChrome.InitializeProcessIdentity()", app);
        Assert.Contains("PersonalAI.Desktop.WinUI.exe", shortcut);
        Assert.Contains("AedaAppIcon.ico", shortcut);
        Assert.Contains("AEDA.LocalIntelligence", shortcut);
        Assert.DoesNotContain("dotnet.exe", shortcut, StringComparison.OrdinalIgnoreCase);
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

        Assert.Contains("ms-appx:///Assets/AedaAppIcon.svg", shell);
        Assert.Contains("ms-appx:///Assets/AedaAppIcon.svg", assist);
        Assert.DoesNotContain("ms-appx:///Assets/AedaLogo.svg", assist);
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
    public void NavigationUsesOneRoundedSelectionTemplateWithoutIndicatorStrips()
    {
        var shell = XDocument.Load(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Views", "MainWindow.xaml"));
        var resources = XDocument.Load(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Themes", "ThemeResources.xaml"));
        var navigationItems = shell.Descendants()
            .Where(element => element.Name.LocalName == "RadioButton" &&
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Style" &&
                    attribute.Value.Contains("AedaNavigationButtonStyle", StringComparison.Ordinal)))
            .ToArray();

        Assert.Equal(4, navigationItems.Length);
        Assert.All(navigationItems, item =>
        {
            Assert.Equal("AedaPrimaryNavigation", item.Attributes().Single(attribute =>
                attribute.Name.LocalName == "GroupName").Value);
            Assert.Contains("Is", item.Attributes().Single(attribute =>
                attribute.Name.LocalName == "IsChecked").Value);
        });
        Assert.DoesNotContain(shell.Descendants(), element =>
            element.Name.LocalName == "Border" &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Width" && attribute.Value == "3") &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Height" && attribute.Value == "24"));
        Assert.Contains(resources.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key" &&
                attribute.Value == "AedaNavigationButtonStyle"));
        Assert.Contains(resources.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" &&
                attribute.Value == "SelectionSurface") &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "CornerRadius"));
        Assert.DoesNotContain("SelectionIndicator", resources.ToString());
    }

    [Fact]
    public void EveryThemeUsesTheSameResponsiveEqualCardGrid()
    {
        var dashboard = XDocument.Load(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Controls", "AedaDashboardView.xaml"));
        var text = dashboard.ToString();
        var grids = dashboard.Descendants()
            .Where(element => element.Name.LocalName == "ContentControl" &&
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "ContentTemplate" &&
                    attribute.Value.Contains("ModuleGridTemplate", StringComparison.Ordinal)))
            .ToArray();
        var layout = dashboard.Descendants().Single(element =>
            element.Name.LocalName == "UniformGridLayout");
        var card = dashboard.Descendants().Single(element =>
            element.Name.LocalName == "Button" &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Height" &&
                attribute.Value.Contains("AedaModuleCardHeight", StringComparison.Ordinal)));

        Assert.Equal(4, grids.Length);
        Assert.All(grids, grid => Assert.Contains(
            "ModuleDashboard.AvailableTiles",
            grid.Attributes().Single(attribute => attribute.Name.LocalName == "Content").Value));
        Assert.Equal("2", layout.Attributes().Single(attribute =>
            attribute.Name.LocalName == "MaximumRowsOrColumns").Value);
        Assert.Equal("360", layout.Attributes().Single(attribute =>
            attribute.Name.LocalName == "MinItemWidth").Value);
        Assert.Equal("190", layout.Attributes().Single(attribute =>
            attribute.Name.LocalName == "MinItemHeight").Value);
        Assert.Equal("Fill", layout.Attributes().Single(attribute =>
            attribute.Name.LocalName == "ItemsStretch").Value);
        Assert.NotNull(card);
        Assert.DoesNotContain("ModuleDashboard.CodeTile", text);
        Assert.DoesNotContain("ModuleDashboard.MemoryTile", text);
        Assert.DoesNotContain("ModuleDashboard.ResearchTile", text);
        Assert.DoesNotContain("ModuleDashboard.TaskCenterTile", text);
        Assert.DoesNotContain("Grid.ColumnSpan", text);
        Assert.DoesNotContain("Grid.RowSpan", text);
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

    [Fact]
    public void EmptyStatesAndDeferredModulesRemainIntentionalAndBounded()
    {
        var shell = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Views", "MainWindow.xaml"));
        var dashboard = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Controls", "AedaDashboardView.xaml"));
        var memory = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Controls", "AedaMemoryWorkspaceView.xaml"));
        var research = File.ReadAllText(Path.Combine(
            RepositoryRoot, "PersonalAI.Desktop.WinUI", "Controls", "AedaResearchWorkspaceView.xaml"));

        Assert.Contains("HasNoStoredContent", memory);
        Assert.Contains("HasStoredContent", memory);
        Assert.Contains("HasSelectedMemory", memory);
        Assert.Contains("HasNoSelectedReport", research);
        Assert.Contains("HasSelectedReport", research);
        Assert.Contains("DeferredCardTemplate", dashboard);
        Assert.Contains("ToolTipService.ToolTip=\"{Binding DisplayName}\"", dashboard);
        Assert.DoesNotContain("TextTrimming=\"CharacterEllipsis\"", dashboard[dashboard.IndexOf("DeferredCardTemplate", StringComparison.Ordinal)..dashboard.IndexOf("ActivityTemplate", StringComparison.Ordinal)]);
        Assert.DoesNotContain("Command=\"{Binding OpenDashboardCommand}\" Content=\"Dashboard\"", shell);
        Assert.DoesNotContain("Command=\"{Binding OpenChatCommand}\" Content=\"Chat\"", shell);
    }
}
