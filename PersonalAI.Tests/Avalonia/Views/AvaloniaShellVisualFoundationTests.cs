using System.Runtime.CompilerServices;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.Avalonia.Themes;

namespace PersonalAI.Tests.Avalonia.Views;

public sealed class AvaloniaShellVisualFoundationTests
{
    [Fact]
    public void LockedThemesExposeTheSameCompleteSemanticPalette()
    {
        var themes = new[]
        {
            ThemePreference.SystemMica,
            ThemePreference.MineralStone,
            ThemePreference.SharpAlmond
        };

        foreach (var theme in themes)
        {
            Assert.All(SemanticColors(AvaloniaThemeManager.GetPalette(theme)),
                color => Assert.Matches("^#[0-9A-Fa-f]{6}$", color));
        }

        Assert.False(AvaloniaThemeManager.GetPalette(ThemePreference.SystemMica).IsDark);
        Assert.False(AvaloniaThemeManager.GetPalette(ThemePreference.MineralStone).IsDark);
        Assert.True(AvaloniaThemeManager.GetPalette(ThemePreference.SharpAlmond).IsDark);
    }

    [Fact]
    public void LockedThemeTokensMatchTheApprovedHandoffExactly()
    {
        Assert.Equal(
        [
            "#EEF0F2", "#FFFFFF", "#EEF0F2", "#FFFFFF", "#FFFFFF", "#F6F7F9",
            "#DCE1E6", "#1B222A", "#5B6572", "#8A93A0", "#4C6FA0", "#E4EBF3",
            "#1B222A", "#4C6FA0", "#2E86A8", "#B4842A", "#3E8F5B", "#B4483E"
        ], LockedTokens(AvaloniaThemeManager.GetPalette(ThemePreference.SystemMica)));

        Assert.Equal(
        [
            "#EFEAE1", "#FBF7F0", "#EFEAE1", "#FBF7F0", "#FBF7F0", "#F4EEE3",
            "#E0D6C4", "#2B2620", "#6B6255", "#9A9082", "#6B7F5C", "#E7ECDF",
            "#2B2620", "#6B7F5C", "#3B7F86", "#B4842A", "#5C7A3E", "#A85C42"
        ], LockedTokens(AvaloniaThemeManager.GetPalette(ThemePreference.MineralStone)));

        Assert.Equal(
        [
            "#15131A", "#1D1B23", "#15131A", "#1D1B23", "#1D1B23", "#24222C",
            "#34313D", "#F1EEF6", "#A9A5B4", "#77738A", "#9C8CE0", "#2C2740",
            "#17151C", "#B3A6EA", "#5FC2D9", "#E0A94B", "#6FCB8F", "#E08079"
        ], LockedTokens(AvaloniaThemeManager.GetPalette(ThemePreference.SharpAlmond)));
    }

    [Fact]
    public void SharedFoundationDefinesShellMetricsAndSemanticResources()
    {
        var foundation = ReadAvaloniaSource("Styles", "Foundations.axaml");
        var requiredKeys = new[]
        {
            "WindowBackgroundBrush", "ShellSurfaceBrush", "ContentSurfaceBrush",
            "ElevatedSurfaceBrush", "CardSurfaceBrush", "SubtleSurfaceBrush",
            "SurfaceAltBrush", "AccentSoftBrush", "AccentTextBrush",
            "PrimaryTextBrush", "SecondaryTextBrush", "MutedTextBrush",
            "TertiaryTextBrush",
            "BorderBrush", "StrongBorderBrush", "AccentBrush", "AccentHoverBrush",
            "AccentPressedBrush", "SuccessBrush", "WarningBrush", "ErrorBrush",
            "ListeningBrush", "FocusRingBrush", "TopShellHeight", "BottomShellHeight",
            "ShellIconSize", "InlineIconSize", "MinimumTargetSize",
            "NavigationTargetSize", "ShellElevation"
        };

        Assert.All(requiredKeys, key => Assert.Contains($"x:Key=\"{key}\"", foundation));
    }

    [Fact]
    public void MainWindowUsesSplitBarsAndKeepsEveryExistingRoute()
    {
        var markup = ReadAvaloniaSource("MainWindow.axaml");
        var source = ReadAvaloniaSource("MainWindow.axaml.cs");
        var composition = ReadAvaloniaSource("Composition", "AvaloniaAppComposition.cs");

        Assert.Contains("x:Name=\"TopShellRegion\"", markup);
        Assert.Contains("x:Name=\"BottomShellRegion\"", markup);
        Assert.DoesNotContain("ColumnDefinitions=\"232,*\"", markup);
        Assert.Contains("DashboardNavItem.Content = CreateNavigationContent(\"Home\", \"home\")", source);
        Assert.Contains("ChatNavItem.Content = CreateNavigationContent(\"Chat\", \"chat\")", source);
        Assert.Contains("screens = screens.OrderBy(ScreenOrder).ToArray()", source);
        Assert.Contains("foreach (var screen in screens)", source);
        Assert.All(new[]
        {
            "aeda-code", "aeda-memory", "aeda-research", "aeda-task-center",
            "aeda-assist", "settings"
        }, route => Assert.Contains($"\"{route}\"", composition));
    }

    [Fact]
    public void NavigationAndAssistHaveAccessibleNonColorStateSemantics()
    {
        var navigation = ReadAvaloniaSource("Styles", "Navigation.axaml");
        var assistStyle = ReadAvaloniaSource("Styles", "Assist.axaml");
        var assistView = ReadAvaloniaSource("Views", "Assist", "AssistView.axaml");

        Assert.Contains("ListBox.shellNavigation ListBoxItem:selected", navigation);
        Assert.Contains("FontWeight\" Value=\"SemiBold", navigation);
        Assert.Contains("Border.selectionIndicator", navigation);
        Assert.Contains("x:Name=\"AssistLauncherGeometry\"", assistView);
        Assert.Contains("AutomationProperties.Name=\"Ask AEDA\"", assistView);
        Assert.All(new[] { "listening", "thinking", "actionReady", "error" },
            state => Assert.Contains($"assistLauncher.{state}", assistStyle));
    }

    [Fact]
    public void NavigationRetainsIconAndLabelAtEveryBreakpoint()
    {
        var markup = ReadAvaloniaSource("MainWindow.axaml");
        var source = ReadAvaloniaSource("MainWindow.axaml.cs");
        var navigation = ReadAvaloniaSource("Styles", "Navigation.axaml");
        var controls = ReadAvaloniaSource("Styles", "Controls.axaml");

        Assert.All(new[]
        {
            "home", "chat", "aeda-code", "aeda-memory", "aeda-research",
            "aeda-task-center", "aeda-assist", "settings"
        }, route => Assert.Contains($"[\"{route}\"]", source));
        Assert.Contains("Classes = { \"navIcon\" }", source);
        Assert.Contains("Classes = { \"navLabel\" }", source);
        Assert.Contains("Window.compact ListBox.shellNavigation TextBlock.navLabel", navigation);
        Assert.DoesNotContain("IsVisible=\"False\"", navigation);
        Assert.Contains("NavigationTargetSize", navigation);
        Assert.Contains("screen.Route == \"settings\"", source);
        Assert.Contains("ListBoxItem.lowFrequency", navigation);
        Assert.Contains("ListBoxItem:pointerover", navigation);
        Assert.Contains("ListBoxItem:pressed", navigation);
        Assert.Contains("ListBoxItem:disabled", navigation);
        Assert.Contains("ListBoxItem:focus-visible", controls);
        Assert.Contains("AutomationProperties.Name=\"Home\"", markup);
        Assert.Contains("AutomationProperties.Name=\"General chat\"", markup);
    }

    [Fact]
    public void ResponsiveShellGeometryMatchesWideMediumAndCompactHandoff()
    {
        var markup = ReadAvaloniaSource("MainWindow.axaml");
        var source = ReadAvaloniaSource("MainWindow.axaml.cs");
        var navigation = ReadAvaloniaSource("Styles", "Navigation.axaml");

        Assert.Contains("<RowDefinition Height=\"64\" />", markup);
        Assert.Contains("<RowDefinition Height=\"72\" />", markup);
        Assert.Contains("new GridLength(compact ? 56 : 64)", source);
        Assert.Contains("new GridLength(compact ? 56 : medium ? 64 : 72)", source);
        Assert.Contains("compact ? 16 : medium ? 24 : 32", source);
        Assert.Contains("Padding\" Value=\"16,10\"", navigation);
        Assert.Contains("Padding\" Value=\"13,9\"", navigation);
        Assert.Contains("Padding\" Value=\"10,7\"", navigation);
    }

    [Fact]
    public void TopBarUsesRepositoryMarkAndNeverLooksEditable()
    {
        var markup = ReadAvaloniaSource("MainWindow.axaml");

        Assert.Contains("Source=\"/Assets/AedaAppIcon.ico\"", markup);
        Assert.Contains("AutomationProperties.Name=\"AEDA mark\"", markup);
        Assert.DoesNotContain("Text=\"A\"", markup);
        Assert.DoesNotContain("<TextBox", markup);
        Assert.DoesNotContain("PlaceholderText", markup);
    }

    [Fact]
    public void ShellDoesNotExposeADeadGlobalSearchAndStillAttachesTextScaling()
    {
        var markup = ReadAvaloniaSource("MainWindow.axaml");
        var app = ReadAvaloniaSource("App.axaml.cs");

        Assert.DoesNotContain("<TextBox", markup);
        Assert.Contains("x:Name=\"PageTitle\"", markup);
        Assert.Contains("_textScaleManager?.Attach(window)", app);
    }

    [Fact]
    public void StartupIdentityRemainsCanonicalAndWinUiIsNotReintroduced()
    {
        var root = GetRepositoryRoot();
        var singleInstance = File.ReadAllText(Path.Combine(
            root, "PersonalAI.Infrastructure", "Windows", "WindowsSingleInstanceService.cs"));
        var processIdentity = ReadAvaloniaSource(
            "Platform", "Windows", "AvaloniaWindowsProcessIdentity.cs");

        Assert.Contains("Local\\\\AEDA.SingleInstance", singleInstance);
        Assert.Contains("AEDA.LocalIntelligence", processIdentity);
        Assert.False(Directory.Exists(Path.Combine(root, "PersonalAI.Desktop.WinUI")));
    }

    private static string[] SemanticColors(AedaPalette palette) =>
    [
        palette.WindowBackground, palette.ShellSurface, palette.ContentSurface,
        palette.ElevatedSurface, palette.CardSurface, palette.SubtleSurface,
        palette.SurfaceAlt, palette.AccentSoft, palette.AccentText,
        palette.PrimaryText, palette.SecondaryText, palette.MutedText,
        palette.Border, palette.StrongBorder, palette.Accent, palette.AccentHover,
        palette.AccentPressed, palette.Success, palette.Warning, palette.Error,
        palette.Listening, palette.FocusRing
    ];

    private static string[] LockedTokens(AedaPalette palette) =>
    [
        palette.WindowBackground, palette.ShellSurface, palette.ContentSurface,
        palette.ElevatedSurface, palette.CardSurface, palette.SurfaceAlt,
        palette.Border, palette.PrimaryText, palette.SecondaryText, palette.MutedText,
        palette.Accent, palette.AccentSoft, palette.AccentText, palette.FocusRing,
        palette.Listening, palette.Warning, palette.Success, palette.Error
    ];

    private static string ReadAvaloniaSource(params string[] relativePath)
    {
        var path = Path.Combine(
            [GetRepositoryRoot(), "PersonalAI.Desktop.Avalonia", .. relativePath]);
        Assert.True(File.Exists(path), $"Avalonia source not found at {path}");
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot(
        [CallerFilePath] string testFilePath = "")
    {
        var directory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(directory, "..", "..", ".."));
    }
}
