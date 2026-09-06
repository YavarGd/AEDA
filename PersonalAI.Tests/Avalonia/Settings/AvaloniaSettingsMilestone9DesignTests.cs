using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PersonalAI.Desktop.Avalonia.Views.Settings;

namespace PersonalAI.Tests.Avalonia.Settings;

public sealed partial class AvaloniaSettingsMilestone9DesignTests
{
    [Fact]
    public void ViewKeepsTheCompiledSettingsContextAndExactlySevenCategories()
    {
        var view = ReadSettingsXaml();
        var source = view.ToString();

        Assert.Equal("vm:SettingsViewModel", Attribute(view.Root!, "DataType"));
        Assert.Equal("Settings", AutomationAttribute(view.Root!, "Name"));
        Assert.All(new[]
        {
            "AppearancePanel", "AssistPanel", "WindowPanel", "ProviderPanel",
            "PrivacyPanel", "WorkspacesPanel", "AdvancedPanel"
        }, name => Assert.NotNull(Named(view, name)));
        Assert.DoesNotContain("Content=\"General\"", source);
        Assert.DoesNotContain("Content=\"Hotkey\"", source);
    }

    [Fact]
    public void DormantAndStubbedSettingsAreNotSurfaced()
    {
        var source = ReadSettingsSource("SettingsView.axaml");
        string[] omitted =
        [
            "LaunchDestination", "PreserveComposerDraftBetweenHideShow",
            "ConfirmBeforeClearingAllContext", "CompactSidebar", "ShowMessageMetadata",
            "HotkeyControl", "HotkeyAlt", "HotkeyShift", "HotkeyWindows", "HotkeyKey",
            "ApplyHotkeyCommand", "ResetHotkeyCommand", "RememberWindowPosition",
            "ResetWindowPositionCommand", "ResetAllCommand", "Voice", "MemoryRag"
        ];

        Assert.All(omitted, item => Assert.DoesNotContain(item, source, StringComparison.Ordinal));
    }

    [Fact]
    public void AppearanceUsesOnlyTheFourExistingThemesAndImmediateClickPath()
    {
        var source = ReadSettingsSource("SettingsView.axaml");
        var code = ReadSettingsSource("SettingsView.axaml.cs");
        var themeTags = ReadSettingsXaml().Descendants()
            .Where(element => element.Name.LocalName == "RadioButton")
            .Select(element => Attribute(element, "Tag"))
            .Where(tag => tag is "SystemMica" or "Graphite" or "MineralStone" or "SharpAlmond")
            .Select(tag => tag!)
            .ToArray();

        Assert.Equal(["SystemMica", "Graphite", "MineralStone", "SharpAlmond"], themeTags);
        Assert.Equal(4, Count(source, "Click=\"OnThemeClick\""));
        Assert.Contains("_themeManager?.Apply(theme)", code);
        Assert.Contains("viewModel.Theme = theme", code);
        Assert.DoesNotContain("new Graphite", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssistAndWindowControlsDescribeTheirRealEffectTiming()
    {
        var source = ReadSettingsSource("SettingsView.axaml");

        Assert.Contains("IsChecked=\"{Binding AssistPillEnabled}\"", source);
        Assert.Contains("Value=\"{Binding AssistPillResponsePreviewCharacters}\"", source);
        Assert.Contains("Minimum=\"200\"", source);
        Assert.Contains("Maximum=\"4000\"", source);
        Assert.True(Count(source, "Takes effect after restarting AEDA.") >= 2);
        Assert.Contains("IsChecked=\"{Binding UniversalSelectionFallbackEnabled}\"", source);
        Assert.Contains("Applies to the next selection.", source);
        Assert.Contains("ItemsSource=\"{Binding CloseBehaviorOptions}\"", source);
        Assert.Contains("SelectedItem=\"{Binding CloseBehavior}\"", source);
        Assert.Contains("IsChecked=\"{Binding StartMinimizedToTray}\"", source);
        Assert.Contains("Applies on the next application launch.", source);
    }

    [Fact]
    public void LaunchAtSignInStaysSupportedGatedAndExplicit()
    {
        var launch = Named(ReadSettingsXaml(), "WindowPanel").Descendants()
            .Single(element => Attribute(element, "Content") == "Launch AEDA at sign-in");
        var source = ReadSettingsSource("SettingsView.axaml");

        Assert.Equal("{Binding LaunchAtSignIn}", Attribute(launch, "IsChecked"));
        Assert.Equal("{Binding IsStartupRegistrationSupported}", Attribute(launch, "IsEnabled"));
        Assert.Equal("{Binding ToggleStartupCommand}", Attribute(launch, "Command"));
        Assert.Contains("IsVisible=\"{Binding !IsStartupRegistrationSupported}\"", source);
        Assert.Contains("Launch at sign-in is unavailable for this host.", source);
    }

    [Fact]
    public void ProviderAndFiveModelRolesKeepTheirExistingPaths()
    {
        var source = ReadSettingsSource("SettingsView.axaml");
        var code = ReadSettingsSource("SettingsView.axaml.cs");

        Assert.Contains("x:Name=\"ProviderPicker\"", source);
        Assert.Contains("Command=\"{Binding RefreshModelsCommand}\"", source);
        Assert.Contains("Command=\"{Binding ResetModelAssignmentsCommand}\"", source);
        Assert.Equal(4, Count(source, "ItemsSource=\"{Binding InstalledModels}\""));
        Assert.Equal(1, Count(source, "ItemsSource=\"{Binding VisionModels}\""));
        Assert.All(new[] { "GeneralModel", "CodingModel", "VisionModel", "FastModel", "ReasoningModel" },
            model => Assert.Contains($"SelectedItem=\"{{Binding {model}}}\"", source));
        Assert.Contains("ProviderRouting.SelectedChatProvider", code);
        Assert.Contains("_settingsService.SaveAsync", code);
        Assert.Contains("await viewModel.RefreshModelsAsync()", code);
    }

    [Fact]
    public void RawOllamaFailureIsSanitizedOnlyForPresentation()
    {
        var source = ReadSettingsSource("SettingsView.axaml");

        Assert.Contains("SettingsPresentationConverters.SafeProviderStatus", source);
        Assert.Equal(
            "Ollama is unavailable. Check that Ollama is running.",
            SettingsPresentationConverters.SanitizeProviderStatus(
                "Ollama models unavailable: connection refused at localhost:11434"));
        Assert.Equal(
            "Ollama returned no installed models.",
            SettingsPresentationConverters.SanitizeProviderStatus(
                "Ollama returned no installed models."));
        Assert.Equal(
            "Loaded 2 installed model(s).",
            SettingsPresentationConverters.SanitizeProviderStatus(
                "Loaded 2 installed model(s)."));
    }

    [Fact]
    public void StatusMessageIsTheOnlyUnlabeledPoliteLiveRegion()
    {
        var view = ReadSettingsXaml();
        var provider = Named(view, "ProviderPanel").ToString();
        var workspaces = Named(view, "WorkspacesPanel").ToString();
        var liveRegion = Assert.Single(
            view.Descendants(),
            element => AutomationAttribute(element, "LiveSetting") == "Polite");

        Assert.Equal("{Binding StatusMessage}", Attribute(liveRegion, "Text"));
        Assert.Null(AutomationAttribute(liveRegion, "Name"));
        Assert.Null(AutomationAttribute(liveRegion, "LabeledBy"));
        Assert.Null(AutomationAttribute(liveRegion, "HelpText"));
        Assert.Contains("ModelRefreshStatus", provider);
        Assert.Contains("Workspaces.StatusMessage", workspaces);
        Assert.Contains("Workspaces.BusyMessage", workspaces);
        Assert.DoesNotContain("LiveSetting", provider);
        Assert.DoesNotContain("LiveSetting", workspaces);
    }

    [Fact]
    public void LocalResultStatusesFeedTheSoleAnnouncerSafely()
    {
        var code = ReadSettingsSource("SettingsView.axaml.cs");
        var model = MethodSlice(
            code,
            "private void OnSettingsPropertyChanged",
            "private void OnWorkspacePropertyChanged");
        var workspace = MethodSlice(
            code,
            "private void OnWorkspacePropertyChanged",
            "private async void OnAttachedToVisualTree");

        Assert.Contains("nameof(SettingsViewModel.ModelRefreshStatus)", model);
        Assert.Contains("SettingsPresentationConverters.SanitizeProviderStatus", model);
        Assert.Contains("viewModel.StatusMessage = status", model);
        Assert.DoesNotContain("StatusMessage = viewModel.ModelRefreshStatus", model);
        Assert.Contains("nameof(WorkspaceManagementViewModel.StatusMessage)", workspace);
        Assert.Contains("_statusViewModel.StatusMessage = status", workspace);
        Assert.DoesNotContain("BusyMessage", model);
        Assert.DoesNotContain("BusyMessage", workspace);
    }

    [Fact]
    public void AutomaticAndProviderRefreshesSuppressCompetingAnnouncements()
    {
        var code = ReadSettingsSource("SettingsView.axaml.cs");
        var attach = MethodSlice(
            code,
            "private async void OnAttachedToVisualTree",
            "private void OnThemeClick");
        var provider = MethodSlice(
            code,
            "private async void OnProviderSelectionChanged",
            "private async void OnAddWorkspaceClick");

        Assert.Contains("_suppressStatusBridge = true", attach);
        Assert.Contains("await viewModel.Workspaces.RefreshAsync()", attach);
        Assert.Contains("await viewModel.RefreshModelsAsync()", attach);
        Assert.Contains("finally", attach);
        Assert.Contains("_suppressStatusBridge = false", attach);
        Assert.Contains("viewModel.StatusMessage = \"Chat provider updated.\"", provider);
        Assert.Contains("_suppressModelStatusBridge = true", provider);
        Assert.Contains("await viewModel.RefreshModelsAsync()", provider);
        Assert.Contains("finally", provider);
        Assert.Contains("_suppressModelStatusBridge = false", provider);
    }

    [Fact]
    public void StatusBridgeRewiresWithoutChangingResetAnnouncements()
    {
        var code = ReadSettingsSource("SettingsView.axaml.cs");
        var dataContext = MethodSlice(
            code,
            "private void OnDataContextChanged",
            "private void AttachStatusBridge");
        var attach = MethodSlice(
            code,
            "private void AttachStatusBridge",
            "private void DetachStatusBridge");
        var detach = MethodSlice(
            code,
            "private void DetachStatusBridge",
            "private void OnSettingsPropertyChanged");
        var settingsViewModel = Read(
            "PersonalAI.Desktop.Presentation",
            "ViewModels",
            "SettingsViewModel.cs");
        var reset = MethodSlice(
            settingsViewModel,
            "public async Task ResetModelAssignmentsAsync",
            "public async Task ToggleStartupAsync");

        Assert.True(
            dataContext.IndexOf("DetachStatusBridge()", StringComparison.Ordinal) <
            dataContext.IndexOf("AttachStatusBridge(viewModel)", StringComparison.Ordinal));
        Assert.Contains("viewModel.PropertyChanged += OnSettingsPropertyChanged", attach);
        Assert.Contains("viewModel.Workspaces.PropertyChanged += OnWorkspacePropertyChanged", attach);
        Assert.Contains("_statusViewModel.PropertyChanged -= OnSettingsPropertyChanged", detach);
        Assert.Contains("_statusViewModel.Workspaces.PropertyChanged -= OnWorkspacePropertyChanged", detach);
        Assert.Contains("Model assignments reset to built-in defaults.", reset);
        Assert.Contains("Model assignments reset to detected defaults.", reset);
        Assert.DoesNotContain("ResetModelAssignments", code);
    }

    [Fact]
    public void ContextAndPrivacyExposeOnlyVerifiedControls()
    {
        var privacy = Named(ReadSettingsXaml(), "PrivacyPanel").ToString();

        Assert.Contains("ExcludedApplicationsText", privacy);
        Assert.Contains("IncludeExecutablePathInProviderMetadata", privacy);
        Assert.Contains("IncludeWindowTitleInProviderContext", privacy);
        Assert.DoesNotContain("ClearAttachmentsAfterSuccessfulSend", privacy);
        Assert.DoesNotContain("permission", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("telemetry", privacy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdvancedSurfacesOnlyFieldsWithCurrentRuntimeConsumers()
    {
        var advanced = Named(ReadSettingsXaml(), "AdvancedPanel").ToString();
        var assistHost = Read("PersonalAI.Desktop.Presentation", "Services", "AssistPillHost.cs");

        Assert.Contains("MaxTotalTextContextCharacters", advanced);
        Assert.Contains("MaxIndividualClipboardCharacters", advanced);
        Assert.Contains("ScreenshotMaxPayloadBytes", advanced);
        Assert.Contains("settings.Context.MaxTotalTextContextCharacters", assistHost);
        Assert.Contains("settings.Context.MaxIndividualClipboardCharacters", assistHost);
        Assert.Contains("settingsService.Current.Context.ScreenshotMaxPayloadBytes", assistHost);
        Assert.DoesNotContain("MaxAttachedContextItems", advanced);
        Assert.DoesNotContain("ScreenshotThumbnailMaxEdge", advanced);
        Assert.DoesNotContain("ClearAttachmentsAfterSuccessfulSend", advanced);
        Assert.Contains("VisionPatternsText", advanced);
    }

    [Fact]
    public void AdvancedRangesAndMaintenanceBindingsUseRepositoryAuthority()
    {
        var advanced = Named(ReadSettingsXaml(), "AdvancedPanel").ToString();

        Assert.Contains("Minimum=\"1000\" Maximum=\"100000\"", advanced);
        Assert.Contains("Minimum=\"500\" Maximum=\"50000\"", advanced);
        Assert.Contains("Minimum=\"262144\" Maximum=\"20971520\"", advanced);
        Assert.Contains("Text=\"{Binding SettingsPath}\"", advanced);
        Assert.Contains("Text=\"{Binding SchemaVersionDisplay}\"", advanced);
        Assert.Contains("Command=\"{Binding OpenSettingsFolderCommand}\"", advanced);
        Assert.DoesNotContain("%LocalAppData%", advanced, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("settings.json", advanced, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"Settings schema:\s*\d", advanced);
    }

    [Fact]
    public void WorkspacesKeepExistingCommandsDialogsAndOneStatusSource()
    {
        var workspace = Named(ReadSettingsXaml(), "WorkspacesPanel").ToString();
        var code = ReadSettingsSource("SettingsView.axaml.cs");

        Assert.Contains("Workspaces.PickWorkspaceFolderCommand", workspace);
        Assert.Contains("Workspaces.RevalidateAllCommand", workspace);
        Assert.Contains("Workspaces.RefreshCommand", workspace);
        Assert.Contains("Workspaces.CancelPendingWorkspaceCommand", workspace);
        Assert.Contains("OnAddWorkspaceClick", workspace);
        Assert.Contains("OnRenameWorkspaceClick", workspace);
        Assert.Contains("OnRevalidateWorkspaceClick", workspace);
        Assert.Contains("OnRemoveWorkspaceClick", workspace);
        Assert.Contains("Workspaces.BusyMessage", workspace);
        Assert.Equal(1, Count(workspace, "Workspaces.StatusMessage"));
        Assert.Equal(0, Count(workspace, "No workspaces registered."));
        Assert.Contains("WorkspaceDialog.ConfirmAsync", code);
        Assert.Contains("WorkspaceDialog.RenameAsync", code);
    }

    [Fact]
    public void ResponsiveLayoutsUseOnlyTheExistingShellModes()
    {
        var wide = SettingsView.ResolveLayout(compact: false, medium: false);
        var medium = SettingsView.ResolveLayout(compact: false, medium: true);
        var compact = SettingsView.ResolveLayout(compact: true, medium: false);
        var code = ReadSettingsSource("SettingsView.axaml.cs");
        var responsive = MethodSlice(code, "public void ApplyResponsiveMode", "internal static (");
        var shell = Read("PersonalAI.Desktop.Avalonia", "MainWindow.axaml.cs");

        Assert.Equal((32, 1080, 224, 24, 28), wide);
        Assert.Equal((24, 900, 0, 20, 24), medium);
        Assert.Equal((16, 640, 0, 16, 22), compact);
        Assert.Contains("settingsRoute.ApplyResponsiveMode(compact, medium)", shell);
        Assert.Contains("CategoryRail.IsVisible = !compact && !medium", responsive);
        Assert.Contains("MediumCategoryNavigation.IsVisible = medium", responsive);
        Assert.DoesNotContain("RefreshAsync", responsive);
        Assert.DoesNotContain("Save", responsive);
        Assert.DoesNotContain("Command", responsive);
        Assert.DoesNotContain(".Focus()", responsive);
    }

    [Fact]
    public void CategorySelectionIsViewLocalAndDoesNotInvokeRuntimeBehavior()
    {
        var code = ReadSettingsSource("SettingsView.axaml.cs");
        var click = MethodSlice(code, "private void OnCategoryClick", "private async void OnProviderSelectionChanged");

        Assert.Contains("private SettingsCategory _selectedCategory", code);
        Assert.Contains("_selectedCategory = category", click);
        Assert.Contains("UpdateCategoryPresentation()", click);
        Assert.DoesNotContain("Save", click);
        Assert.DoesNotContain("Refresh", click);
        Assert.DoesNotContain("Command", click);
        Assert.DoesNotContain("DataContext", click);
    }

    [Fact]
    public void FocusAndInitialLoadRetainTheirNarrowContracts()
    {
        var code = ReadSettingsSource("SettingsView.axaml.cs");
        var focus = MethodSlice(code, "public void FocusPrimaryAction", "public void ApplyResponsiveMode");
        var attach = MethodSlice(code, "private async void OnAttachedToVisualTree", "private void OnThemeClick");

        Assert.Contains("SystemMicaThemeOption.Focus()", focus);
        Assert.Contains("GetCategoryControl(_selectedCategory, _medium).Focus()", focus);
        Assert.DoesNotContain("ProviderPicker.Focus()", focus);
        Assert.Contains("_loaded", attach);
        Assert.Equal(1, Count(attach, "Workspaces.RefreshAsync"));
        Assert.Equal(1, Count(attach, "RefreshModelsAsync"));
    }

    [Fact]
    public void RedesignUsesOnlyExistingSemanticResourcesAndNoHorizontalScroller()
    {
        var source = ReadSettingsSource("SettingsView.axaml");
        var resources = DynamicResourceRegex().Matches(source)
            .Cast<Match>()
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray();
        string[] allowed =
        [
            "ContentSurfaceBrush", "CardSurfaceBrush", "SubtleSurfaceBrush",
            "SurfaceAltBrush", "BorderBrush", "PrimaryTextBrush",
            "SecondaryTextBrush", "AccentBrush", "AccentSoftBrush"
        ];

        Assert.All(resources, resource => Assert.Contains(resource, allowed));
        Assert.DoesNotContain("AedaBorderBrush", source);
        Assert.DoesNotMatch(HexColorRegex(), source);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", source);
        Assert.DoesNotContain("HorizontalScrollBarVisibility=\"Auto\"", source);
        Assert.True(Count(source, "<WrapPanel") >= 6);
    }

    [GeneratedRegex(@"\{DynamicResource\s+([A-Za-z0-9]+)\}")]
    private static partial Regex DynamicResourceRegex();

    [GeneratedRegex(@"#[0-9A-Fa-f]{3,8}\b")]
    private static partial Regex HexColorRegex();

    private static XDocument ReadSettingsXaml() =>
        XDocument.Parse(ReadSettingsSource("SettingsView.axaml"));

    private static XElement Named(XDocument document, string name) =>
        Assert.Single(document.Descendants(), element => Attribute(element, "Name") == name);

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

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string ReadSettingsSource(string fileName) =>
        Read("PersonalAI.Desktop.Avalonia", "Views", "Settings", fileName);

    private static string Read(params string[] relativePath)
    {
        var path = Path.Combine([GetRepositoryRoot(), .. relativePath]);
        Assert.True(File.Exists(path), $"Source not found at {path}");
        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot([CallerFilePath] string testFilePath = "")
    {
        var settingsDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(settingsDirectory, "..", "..", ".."));
    }
}
