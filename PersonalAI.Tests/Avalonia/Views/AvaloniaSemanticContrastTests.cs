using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.Avalonia.Themes;

namespace PersonalAI.Tests.Avalonia.Views;

public sealed class AvaloniaSemanticContrastTests
{
    private const string ProbeVariable = "AEDA_D05_CONTRAST_PROBE";

    [Fact]
    public void ContrastRatioUsesWcagRelativeLuminance()
    {
        Assert.Equal(21, ContrastRatio("#000000", "#FFFFFF"), 3);
        Assert.Equal(1, ContrastRatio("#5A6570", "#5A6570"), 3);
        Assert.Equal(4, ContrastRatio("#FF0000", "#FFFFFF"), 2);
    }

    [Theory]
    [InlineData(ThemePreference.SystemMica)]
    [InlineData(ThemePreference.Graphite)]
    [InlineData(ThemePreference.MineralStone)]
    [InlineData(ThemePreference.SharpAlmond)]
    public void MutedTextMeetsNormalTextContrastOnRealSurfaces(ThemePreference theme)
    {
        var palette = AvaloniaThemeManager.GetPalette(theme);

        AssertContrast(palette.MutedText, palette.CardSurface, theme, "CardSurface");
        AssertContrast(palette.MutedText, palette.ContentSurface, theme, "ContentSurface");
        AssertContrast(palette.MutedText, palette.SubtleSurface, theme, "SubtleSurface");
        AssertContrast(palette.MutedText, palette.SurfaceAlt, theme, "SurfaceAlt");
        AssertContrast(palette.MutedText, palette.AccentSoft, theme, "AccentSoft");
        AssertContrast(palette.MutedText, palette.ShellSurface, theme, "ShellSurface");
    }

    [Theory]
    [InlineData(ThemePreference.SystemMica)]
    [InlineData(ThemePreference.Graphite)]
    [InlineData(ThemePreference.MineralStone)]
    [InlineData(ThemePreference.SharpAlmond)]
    public void AccentTextMeetsNormalTextContrastAcrossAccentStates(ThemePreference theme)
    {
        var palette = AvaloniaThemeManager.GetPalette(theme);

        AssertContrast(palette.AccentText, palette.Accent, theme, "Accent");
        AssertContrast(palette.AccentText, palette.AccentHover, theme, "AccentHover");
        AssertContrast(palette.AccentText, palette.AccentPressed, theme, "AccentPressed");
    }

    [Theory]
    [InlineData(ThemePreference.SystemMica)]
    [InlineData(ThemePreference.Graphite)]
    [InlineData(ThemePreference.MineralStone)]
    [InlineData(ThemePreference.SharpAlmond)]
    public void PrimarySecondaryAndMutedHierarchyRemainsReadable(ThemePreference theme)
    {
        var palette = AvaloniaThemeManager.GetPalette(theme);
        var surfaces = new[]
        {
            palette.CardSurface,
            palette.ContentSurface,
            palette.ElevatedSurface,
            palette.SubtleSurface,
            palette.SurfaceAlt,
            palette.AccentSoft,
            palette.ShellSurface
        };

        foreach (var surface in surfaces)
        {
            AssertContrast(palette.PrimaryText, surface, theme, "PrimaryText");
            AssertContrast(palette.SecondaryText, surface, theme, "SecondaryText");
        }

        var primary = RelativeLuminance(palette.PrimaryText);
        var secondary = RelativeLuminance(palette.SecondaryText);
        var muted = RelativeLuminance(palette.MutedText);
        Assert.True(palette.IsDark
            ? primary > secondary && secondary > muted
            : primary < secondary && secondary < muted,
            $"{theme} text hierarchy was flattened.");
    }

    [Fact]
    public void HighContrastSemanticTextPaletteRemainsUnchanged()
    {
        var palette = AvaloniaThemeManager.GetPalette(
            ThemePreference.Graphite,
            highContrast: true);

        Assert.Equal("#000000", palette.CardSurface);
        Assert.Equal("#000000", palette.AccentText);
        Assert.Equal("#FFFFFF", palette.PrimaryText);
        Assert.Equal("#FFFFFF", palette.SecondaryText);
        Assert.Equal("#FFFFFF", palette.MutedText);
        Assert.Equal("#00FFFF", palette.Accent);
        Assert.Equal("#FFFFFF", palette.AccentHover);
        Assert.Equal("#00FFFF", palette.AccentPressed);
    }

    [Fact]
    public void SemanticAliasesAndD04SelectedMappingsRemainWired()
    {
        var manager = ReadAvaloniaSource("Themes", "AvaloniaThemeManager.cs");

        Assert.Contains("SetBrush(application, \"TertiaryTextBrush\", palette.MutedText)", manager);
        Assert.Contains("SetBrush(application, \"MutedTextBrush\", palette.MutedText)", manager);
        Assert.Contains("SetBrush(application, \"AedaMetadataBrush\", palette.Metadata)", manager);
        Assert.Contains("SetBrush(application, \"AccentTextBrush\", palette.AccentText)", manager);
        Assert.Contains("SetBrush(application, \"CheckBoxCheckBackgroundFillChecked\", palette.Accent)", manager);
        Assert.Contains("SetBrush(application, \"RadioButtonOuterEllipseCheckedFillPressed\", palette.AccentPressed)", manager);
        Assert.Contains("SetBrush(application, \"TabItemHeaderSelectedPipeFill\", palette.Accent)", manager);
    }

    [Fact]
    public async Task PaletteSwitchingResolvesContrastSafeSemanticBrushes()
    {
        if (Environment.GetEnvironmentVariable(ProbeVariable) == "1")
        {
            RunResourceProbe();
            return;
        }

        await RunProbeInIsolatedProcessAsync();
    }

    private static void RunResourceProbe()
    {
        AppBuilder.Configure<PersonalAI.Desktop.Avalonia.App>()
            .UsePlatformDetect()
            .SetupWithoutStarting();

        var application = Assert.IsType<PersonalAI.Desktop.Avalonia.App>(Application.Current);
        using var manager = new AvaloniaThemeManager(ThemePreference.SystemMica);
        foreach (var theme in AedaThemeCatalog.All)
        {
            manager.Apply(theme.Id);
            var palette = AvaloniaThemeManager.GetPalette(theme.Id);
            AssertBrush(application, "AccentTextBrush", palette.AccentText);
            AssertBrush(application, "MutedTextBrush", palette.MutedText);
            AssertBrush(application, "TertiaryTextBrush", palette.MutedText);
            AssertBrush(application, "AedaMetadataBrush", palette.MutedText);
            AssertContrast(palette.MutedText, palette.CardSurface, theme.Id, "resolved muted");
            AssertContrast(palette.AccentText, palette.Accent, theme.Id, "resolved accent text");
        }
    }

    private static void AssertBrush(Application application, string key, string expected)
    {
        Assert.True(application.TryGetResource(
            key,
            application.ActualThemeVariant,
            out var resource), $"Resource '{key}' did not resolve.");
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(resource);
        Assert.Equal(Color.Parse(expected), brush.Color);
    }

    private static void AssertContrast(
        string foreground,
        string background,
        ThemePreference theme,
        string surface)
    {
        var ratio = ContrastRatio(foreground, background);
        Assert.True(ratio >= 4.5,
            $"{theme} {foreground} on {surface} {background} was {ratio:F3}:1.");
    }

    private static double ContrastRatio(string first, string second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
            (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var color = Color.Parse(hex);
        return 0.2126 * Linear(color.R) +
            0.7152 * Linear(color.G) +
            0.0722 * Linear(color.B);
    }

    private static double Linear(byte value)
    {
        var channel = value / 255d;
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
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
        startInfo.ArgumentList.Add(typeof(AvaloniaSemanticContrastTests).Assembly.Location);
        startInfo.ArgumentList.Add(
            "--TestCaseFilter:FullyQualifiedName=" +
            typeof(AvaloniaSemanticContrastTests).FullName + "." +
            nameof(PaletteSwitchingResolvesContrastSafeSemanticBrushes));
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
