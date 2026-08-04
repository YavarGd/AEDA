using System.Runtime.CompilerServices;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.Avalonia.Themes;

namespace PersonalAI.Tests.Avalonia.Settings;

public sealed class AvaloniaSettingsViewTests
{
    [Fact]
    public void FourCatalogThemesMapToDistinctPalettes()
    {
        var palettes = AedaThemeCatalog.All
            .Select(theme => AvaloniaThemeManager.GetPalette(theme.Id))
            .ToArray();

        Assert.Equal(4, palettes.Length);
        Assert.Equal(4, palettes.Select(palette => palette.Navigation).Distinct().Count());
        Assert.True(AvaloniaThemeManager.GetPalette(ThemePreference.Graphite).IsDark);
        Assert.False(AvaloniaThemeManager.GetPalette(ThemePreference.MineralStone).IsDark);
    }

    [Fact]
    public void HighContrastUsesExplicitNonColorStatusFallbackPalette()
    {
        var palette = AvaloniaThemeManager.GetPalette(
            ThemePreference.SharpAlmond,
            highContrast: true);

        Assert.True(palette.IsDark);
        Assert.Equal("#FFFFFF", palette.Border);
        Assert.Equal("#FFFFFF", palette.Metadata);
        Assert.Equal("#FFFF00", palette.Focus);
    }

    [Fact]
    public void SettingsScreenCoversProviderModelsAndWorkspaceLifecycle()
    {
        var source = ReadAvaloniaSource("Views", "Settings", "SettingsView.axaml");

        Assert.Contains("ProviderPicker", source);
        Assert.Contains("InstalledModels", source);
        Assert.Contains("VisionModels", source);
        Assert.Contains("PickWorkspaceFolderCommand", source);
        Assert.Contains("OnAddWorkspaceClick", source);
        Assert.Contains("OnRenameWorkspaceClick", source);
        Assert.Contains("OnRemoveWorkspaceClick", source);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", source);
        Assert.DoesNotContain("PermissionDialog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PermissionBroker", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FolderPickerAndConfirmationsUseNativeAvaloniaApis()
    {
        var picker = ReadAvaloniaSource(
            "Views",
            "Settings",
            "AvaloniaFolderPickerService.cs");
        var dialog = ReadAvaloniaSource(
            "Views",
            "Dialogs",
            "WorkspaceDialog.cs");

        Assert.Contains("StorageProvider", picker);
        Assert.Contains("OpenFolderPickerAsync", picker);
        Assert.Contains("TryGetLocalPath", picker);
        Assert.DoesNotContain("Microsoft.UI", picker);
        Assert.Contains("ShowDialog<bool>", dialog);
        Assert.Contains("ShowDialog<string?>", dialog);
    }

    [Fact]
    public void SettingsRegistersOnlyThroughTheE1ScreenList()
    {
        var composition = ReadAvaloniaSource(
            "Composition",
            "AvaloniaAppComposition.cs");
        var app = ReadAvaloniaSource("App.axaml.cs");

        Assert.Contains("\"settings\"", composition);
        Assert.Contains("new SettingsViewModel", composition);
        Assert.Contains("new SettingsView(", composition);
        Assert.DoesNotContain("Settings", app);
    }

    private static string ReadAvaloniaSource(
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
        // <repo>/PersonalAI.Tests/Avalonia/Settings/<this file>
        var directory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(directory, "..", "..", ".."));
    }
}
