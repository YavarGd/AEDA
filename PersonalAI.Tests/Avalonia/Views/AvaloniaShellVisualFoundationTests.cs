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
    public void SharedFoundationDefinesShellMetricsAndSemanticResources()
    {
        var foundation = ReadAvaloniaSource("Styles", "Foundations.axaml");
        var requiredKeys = new[]
        {
            "WindowBackgroundBrush", "ShellSurfaceBrush", "ContentSurfaceBrush",
            "ElevatedSurfaceBrush", "CardSurfaceBrush", "SubtleSurfaceBrush",
            "PrimaryTextBrush", "SecondaryTextBrush", "MutedTextBrush",
            "BorderBrush", "StrongBorderBrush", "AccentBrush", "AccentHoverBrush",
            "AccentPressedBrush", "SuccessBrush", "WarningBrush", "ErrorBrush",
            "ListeningBrush", "FocusRingBrush", "TopShellHeight", "BottomShellHeight",
            "ShellIconSize", "MinimumTargetSize", "ShellElevation"
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
        Assert.Contains("Content=\"Home\"", markup);
        Assert.Contains("Content=\"Chat\"", markup);
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
        Assert.Contains("BorderThickness\" Value=\"0,0,0,3", navigation);
        Assert.Contains("x:Name=\"AssistLauncherGeometry\"", assistView);
        Assert.Contains("AutomationProperties.Name=\"Ask AEDA\"", assistView);
        Assert.All(new[] { "listening", "thinking", "ready", "error" },
            state => Assert.Contains($"assistLauncher.{state}", assistStyle));
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
        palette.PrimaryText, palette.SecondaryText, palette.MutedText,
        palette.Border, palette.StrongBorder, palette.Accent, palette.AccentHover,
        palette.AccentPressed, palette.Success, palette.Warning, palette.Error,
        palette.Listening, palette.FocusRing
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
