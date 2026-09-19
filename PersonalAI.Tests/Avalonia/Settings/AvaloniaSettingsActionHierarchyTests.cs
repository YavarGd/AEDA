using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.Avalonia.Themes;

namespace PersonalAI.Tests.Avalonia.Settings;

public sealed class AvaloniaSettingsActionHierarchyTests
{
    private const string ProbeVariable = "AEDA_D10_ACTION_STYLE_PROBE";

    [Fact]
    public void D10_SettingsActionsKeepLabelsBehaviorAndAssignedHierarchy()
    {
        var view = ReadSettingsXaml();

        AssertAction(view, "Refresh models", "actionUtility", "Command", "{Binding RefreshModelsCommand}");
        AssertAction(view, "Reset assignments", "actionCautionary", "Command", "{Binding ResetModelAssignmentsCommand}");
        AssertAction(view, "Choose workspace folder", "actionPrimary", "Command", "{Binding Workspaces.PickWorkspaceFolderCommand}");
        AssertAction(view, "Revalidate all", "actionUtility", "Command", "{Binding Workspaces.RevalidateAllCommand}");
        AssertAction(view, "Refresh", "actionUtility", "Command", "{Binding Workspaces.RefreshCommand}");
        AssertAction(view, "Add workspace", "actionPrimary", "Click", "OnAddWorkspaceClick");
        AssertAction(view, "Cancel", "actionSecondary", "Command", "{Binding Workspaces.CancelPendingWorkspaceCommand}");
        AssertAction(view, "Rename", "actionSecondary", "Click", "OnRenameWorkspaceClick");
        AssertAction(view, "Revalidate", "actionUtility", "Click", "OnRevalidateWorkspaceClick");
        AssertAction(view, "Remove", "actionCautionary", "Click", "OnRemoveWorkspaceClick");
        AssertAction(view, "Open settings folder", "actionUtility", "Command", "{Binding OpenSettingsFolderCommand}");

        Assert.Equal("Refresh provider models", AutomationName(Button(view, "Refresh models")));
        Assert.Equal("Refresh workspaces", AutomationName(Button(view, "Refresh")));
    }

    [Fact]
    public void D10_ConsequenceAndSetupClassesCannotTradePlaces()
    {
        var view = ReadSettingsXaml();

        Assert.All(
            new[] { Button(view, "Reset assignments"), Button(view, "Remove") },
            button =>
            {
                Assert.Equal("actionCautionary", Attribute(button, "Classes"));
                Assert.NotEqual("actionPrimary", Attribute(button, "Classes"));
            });
        Assert.All(
            new[] { Button(view, "Choose workspace folder"), Button(view, "Add workspace") },
            button =>
            {
                Assert.Equal("actionPrimary", Attribute(button, "Classes"));
                Assert.NotEqual("actionCautionary", Attribute(button, "Classes"));
            });
    }

    [Fact]
    public void D10_SharedVocabularyUsesSemanticResourcesAndCompleteStates()
    {
        var controls = Read("PersonalAI.Desktop.Avalonia", "Styles", "Controls.axaml");

        Assert.All(
            new[] { "actionPrimary", "actionSecondary", "actionUtility", "actionCautionary" },
            actionClass =>
            {
                Assert.Contains($"Button.{actionClass}", controls);
                Assert.Contains($"Button.{actionClass}:pointerover", controls);
                Assert.Contains($"Button.{actionClass}:pressed", controls);
                Assert.Contains($"Button.{actionClass}:disabled", controls);
                Assert.Contains($"Button.{actionClass}:focus-visible", controls);
            });
        Assert.All(
            new[]
            {
                "AccentBrush", "AccentTextBrush", "AccentHoverBrush", "AccentPressedBrush",
                "ElevatedSurfaceBrush", "SurfaceAltBrush", "AccentSoftBrush", "BorderBrush",
                "StrongBorderBrush", "PrimaryTextBrush", "SecondaryTextBrush", "ErrorBrush",
                "SubtleSurfaceBrush", "FocusRingBrush"
            },
            resource => Assert.Contains($"{{DynamicResource {resource}}}", controls));
        Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", controls);
    }

    [Fact]
    public async Task D10_ActionStylesResolveAcrossAllFourThemes()
    {
        if (Environment.GetEnvironmentVariable(ProbeVariable) == "1")
        {
            RunStyleProbe();
            return;
        }

        await RunProbeInIsolatedProcessAsync();
    }

    private static void RunStyleProbe()
    {
        AppBuilder.Configure<PersonalAI.Desktop.Avalonia.App>()
            .UsePlatformDetect()
            .SetupWithoutStarting();

        var primary = StyledButton("actionPrimary");
        var secondary = StyledButton("actionSecondary");
        var utility = StyledButton("actionUtility");
        var cautionary = StyledButton("actionCautionary");
        var window = new Window
        {
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Children = { primary, secondary, utility, cautionary }
            }
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        using var manager = new AvaloniaThemeManager(ThemePreference.SystemMica);
        foreach (var theme in AedaThemeCatalog.All)
        {
            manager.Apply(theme.Id);
            Dispatcher.UIThread.RunJobs();
            var palette = AvaloniaThemeManager.GetPalette(theme.Id);
            AssertBrush(primary.Background, palette.Accent);
            AssertBrush(primary.Foreground, palette.AccentText);
            AssertBrush(secondary.Background, palette.ElevatedSurface);
            AssertBrush(secondary.BorderBrush, palette.Border);
            AssertBrush(utility.Foreground, palette.SecondaryText);
            AssertBrush(cautionary.Foreground, palette.Error);
            AssertBrush(cautionary.BorderBrush, palette.Error);
        }

        utility.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.55, utility.Opacity, 2);
        window.Close();
    }

    private static Button StyledButton(string actionClass)
    {
        var button = new Button { Content = actionClass };
        button.Classes.Add(actionClass);
        return button;
    }

    private static void AssertBrush(IBrush? brush, string expected) =>
        Assert.Equal(Color.Parse(expected), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);

    private static void AssertAction(
        XDocument view,
        string label,
        string actionClass,
        string behaviorAttribute,
        string behavior)
    {
        var button = Button(view, label);
        Assert.Equal(actionClass, Attribute(button, "Classes"));
        Assert.Equal(behavior, Attribute(button, behaviorAttribute));
    }

    private static XElement Button(XDocument view, string label) =>
        Assert.Single(view.Descendants(), element =>
            element.Name.LocalName == "Button" && Attribute(element, "Content") == label);

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == localName ||
            attribute.Name.LocalName.EndsWith($".{localName}", StringComparison.Ordinal))?.Value;

    private static string? AutomationName(XElement element) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == "AutomationProperties.Name")?.Value;

    private static XDocument ReadSettingsXaml() =>
        XDocument.Parse(Read(
            "PersonalAI.Desktop.Avalonia", "Views", "Settings", "SettingsView.axaml"));

    private static string Read(params string[] relativePath)
    {
        var path = Path.Combine([GetRepositoryRoot(), .. relativePath]);
        Assert.True(File.Exists(path), $"Source not found at {path}");
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot([CallerFilePath] string testFilePath = "")
    {
        var directory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(directory, "..", "..", ".."));
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
        startInfo.ArgumentList.Add(typeof(AvaloniaSettingsActionHierarchyTests).Assembly.Location);
        startInfo.ArgumentList.Add(
            "--TestCaseFilter:FullyQualifiedName=" +
            typeof(AvaloniaSettingsActionHierarchyTests).FullName + "." +
            nameof(D10_ActionStylesResolveAcrossAllFourThemes));
        startInfo.Environment[ProbeVariable] = "1";

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var output = await standardOutput + await standardError;

        Assert.True(process.ExitCode == 0, output);
    }
}
