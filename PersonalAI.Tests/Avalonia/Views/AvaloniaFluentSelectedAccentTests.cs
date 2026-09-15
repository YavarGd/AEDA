using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.Avalonia.Themes;

namespace PersonalAI.Tests.Avalonia.Views;

public sealed class AvaloniaFluentSelectedAccentTests
{
    private const string ProbeVariable = "AEDA_D04_SELECTED_ACCENT_PROBE";
    private const string ConflictingSystemAccent = "#C2185B";

    [Fact]
    public async Task StockSelectedIndicatorsResolveToEveryAedaPaletteAndUpdateLive()
    {
        if (Environment.GetEnvironmentVariable(ProbeVariable) == "1")
        {
            RunResourceProbe();
            return;
        }

        await RunProbeInIsolatedProcessAsync();
    }

    [Fact]
    public void SelectedAccentOverrideStaysGlobalAndLeavesOtherStateWiringAlone()
    {
        var manager = ReadAvaloniaSource("Themes", "AvaloniaThemeManager.cs");
        var navigation = ReadAvaloniaSource("Styles", "Navigation.axaml");
        var controls = ReadAvaloniaSource("Styles", "Controls.axaml");

        Assert.DoesNotContain("SystemControlHighlightAccentBrush", manager);
        Assert.DoesNotContain("CheckedDisabled", manager);
        Assert.DoesNotContain("RadioButtonOuterEllipseCheckedFillDisabled", manager);
        Assert.Contains("SetBrush(application, \"FocusRingBrush\", palette.FocusRing)", manager);
        Assert.Contains("ListBox.shellNavigation ListBoxItem:selected Border.selectionIndicator", navigation);
        Assert.Contains("Background\" Value=\"{DynamicResource AccentBrush}", navigation);
        Assert.Contains("ListBoxItem:focus-visible", controls);
    }

    private static void RunResourceProbe()
    {
        AppBuilder.Configure<PersonalAI.Desktop.Avalonia.App>()
            .UsePlatformDetect()
            .SetupWithoutStarting();

        var application = Assert.IsType<PersonalAI.Desktop.Avalonia.App>(Application.Current);
        var palette = AvaloniaThemeManager.GetPalette(ThemePreference.SystemMica);
        application.Resources["AccentBrush"] = Brush(palette.Accent);
        var fluentTheme = Assert.Single(application.Styles.OfType<FluentTheme>());
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            fluentTheme.Palettes[variant] = new ColorPaletteResources
            {
                Accent = Color.Parse(ConflictingSystemAccent)
            };
        }

        AssertBrushes(application, palette.Accent, "AccentBrush");
        AssertBrushes(application, ConflictingSystemAccent,
            "ToggleButtonBackgroundChecked",
            "CheckBoxCheckBackgroundFillChecked",
            "RadioButtonOuterEllipseCheckedStroke",
            "RadioButtonOuterEllipseCheckedFill",
            "TabItemHeaderSelectedPipeFill");

        using var manager = new AvaloniaThemeManager(ThemePreference.SystemMica);
        foreach (var theme in AedaThemeCatalog.All)
        {
            manager.Apply(theme.Id);
            AssertSelectedBrushes(application, AvaloniaThemeManager.GetPalette(theme.Id));
        }
    }

    private static SolidColorBrush Brush(string color) =>
        new(Color.Parse(color));

    private static void AssertSelectedBrushes(Application application, AedaPalette palette)
    {
        AssertBrushes(application, palette.Accent,
            "ToggleButtonBackgroundChecked",
            "CheckBoxCheckBackgroundFillChecked",
            "RadioButtonOuterEllipseCheckedStroke",
            "RadioButtonOuterEllipseCheckedFill",
            "TabItemHeaderSelectedPipeFill");
        AssertBrushes(application, palette.AccentHover,
            "ToggleButtonBackgroundCheckedPointerOver",
            "CheckBoxCheckBackgroundStrokeCheckedPointerOver",
            "CheckBoxCheckBackgroundFillCheckedPointerOver",
            "RadioButtonOuterEllipseCheckedStrokePointerOver",
            "RadioButtonOuterEllipseCheckedFillPointerOver");
        AssertBrushes(application, palette.AccentPressed,
            "CheckBoxCheckBackgroundStrokeCheckedPressed",
            "CheckBoxCheckBackgroundFillCheckedPressed",
            "RadioButtonOuterEllipseCheckedStrokePressed",
            "RadioButtonOuterEllipseCheckedFillPressed");
    }

    private static void AssertBrushes(
        Application application,
        string expected,
        params string[] keys)
    {
        var expectedColor = Color.Parse(expected);
        foreach (var key in keys)
        {
            Assert.True(application.TryGetResource(
                key,
                application.ActualThemeVariant,
                out var resource), $"Resource '{key}' did not resolve.");
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(resource);
            Assert.Equal(expectedColor, brush.Color);
        }
    }

    private static async Task RunProbeInIsolatedProcessAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(typeof(AvaloniaFluentSelectedAccentTests).Assembly.Location);
        startInfo.ArgumentList.Add(
            "--TestCaseFilter:FullyQualifiedName=" +
            typeof(AvaloniaFluentSelectedAccentTests).FullName + "." +
            nameof(StockSelectedIndicatorsResolveToEveryAedaPaletteAndUpdateLive));
        startInfo.Environment[ProbeVariable] = "1";

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var output = await standardOutput + await standardError;

        Assert.True(process.ExitCode == 0, output);
    }

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
