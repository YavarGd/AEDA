using Avalonia;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Automation.Peers;
using Avalonia.LogicalTree;
using Avalonia.Media;
using PersonalAI.Core.Settings;
using PersonalAI.Core.Workspaces;
using PersonalAI.Desktop.Avalonia.Themes;
using PersonalAI.Desktop.Avalonia.Views.Settings;
using PersonalAI.Desktop.Presentation.Services;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Tests.Avalonia.Settings;

public sealed class AvaloniaSettingsSelectionBehaviorTests
{
    private const string ProbeVariable = "AEDA_D06_SELECTION_PROBE";

    [Fact]
    public async Task NativeThemeSelectionMatchesClickAndApplicationState()
    {
        if (Environment.GetEnvironmentVariable(ProbeVariable) == "theme")
        {
            RunThemeProbe();
            return;
        }

        await RunProbeInIsolatedProcessAsync(
            nameof(NativeThemeSelectionMatchesClickAndApplicationState),
            "theme");
    }

    [Fact]
    public async Task NativeCategorySelectionStaysSynchronizedAcrossLayouts()
    {
        if (Environment.GetEnvironmentVariable(ProbeVariable) == "category")
        {
            RunCategoryProbe();
            return;
        }

        await RunProbeInIsolatedProcessAsync(
            nameof(NativeCategorySelectionStaysSynchronizedAcrossLayouts),
            "category");
    }

    private static void RunThemeProbe()
    {
        var application = StartApplication();
        var store = new RecordingSettingsService(Settings(ThemePreference.SystemMica));
        var viewModel = CreateViewModel(store);
        using var themeManager = new AvaloniaThemeManager(viewModel.Theme);
        var view = new SettingsView(themeManager, store) { DataContext = viewModel };
        var themeTransitions = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.Theme))
            {
                themeTransitions++;
            }
        };

        AssertTheme(view, application, viewModel, ThemePreference.SystemMica);
        Assert.Equal(0, store.SaveCount);

        var graphite = ThemeButton(view, ThemePreference.Graphite);
        var eventOrder = new List<string>();
        graphite.IsCheckedChanged += (_, _) => eventOrder.Add("checked");
        graphite.Click += (_, _) => eventOrder.Add("click");
        ToggleThroughClickPath(graphite);

        Assert.Equal(["checked", "click"], eventOrder);
        AssertTheme(view, application, viewModel, ThemePreference.Graphite);
        Assert.Equal(1, themeTransitions);
        Assert.Equal(1, store.SaveCount);

        ToggleThroughClickPath(ThemeButton(view, ThemePreference.SystemMica));
        AssertTheme(view, application, viewModel, ThemePreference.SystemMica);
        Assert.Equal(2, themeTransitions);
        Assert.Equal(2, store.SaveCount);

        SelectNatively(ThemeButton(view, ThemePreference.Graphite));
        AssertTheme(view, application, viewModel, ThemePreference.Graphite);
        Assert.Equal(3, themeTransitions);
        Assert.Equal(3, store.SaveCount);

        SelectNatively(ThemeButton(view, ThemePreference.MineralStone));
        AssertTheme(view, application, viewModel, ThemePreference.MineralStone);
        SelectNatively(ThemeButton(view, ThemePreference.SharpAlmond));
        AssertTheme(view, application, viewModel, ThemePreference.SharpAlmond);
        SelectNatively(ThemeButton(view, ThemePreference.SystemMica));
        AssertTheme(view, application, viewModel, ThemePreference.SystemMica);
        Assert.Equal(6, themeTransitions);
        Assert.Equal(6, store.SaveCount);

        SelectNatively(ThemeButton(view, ThemePreference.SystemMica));
        Assert.Equal(6, themeTransitions);
        Assert.Equal(6, store.SaveCount);
    }

    private static void RunCategoryProbe()
    {
        StartApplication();
        var store = new RecordingSettingsService(Settings(ThemePreference.SystemMica));
        var viewModel = CreateViewModel(store);
        using var themeManager = new AvaloniaThemeManager(viewModel.Theme);
        var view = new SettingsView(themeManager, store) { DataContext = viewModel };

        view.ApplyResponsiveMode(compact: false, medium: false);
        AssertCategory(view, "Appearance", compact: false);

        ToggleThroughClickPath(CategoryButton(view, "Assist", medium: false));
        AssertCategory(view, "Assist", compact: false);
        ToggleThroughClickPath(CategoryButton(view, "Appearance", medium: false));
        AssertCategory(view, "Appearance", compact: false);

        SelectNatively(CategoryButton(view, "Provider", medium: false));
        AssertCategory(view, "Provider", compact: false);

        view.ApplyResponsiveMode(compact: false, medium: true);
        AssertCategory(view, "Provider", compact: false);
        SelectNatively(CategoryButton(view, "Privacy", medium: true));
        AssertCategory(view, "Privacy", compact: false);

        view.ApplyResponsiveMode(compact: false, medium: false);
        AssertCategory(view, "Privacy", compact: false);
        SelectNatively(CategoryButton(view, "Privacy", medium: false));
        AssertCategory(view, "Privacy", compact: false);

        view.ApplyResponsiveMode(compact: true, medium: false);
        AssertCategory(view, "Privacy", compact: true);
        view.ApplyResponsiveMode(compact: false, medium: false);
        AssertCategory(view, "Privacy", compact: false);
        Assert.Equal(0, store.SaveCount);
    }

    private static Application StartApplication()
    {
        AppBuilder.Configure<PersonalAI.Desktop.Avalonia.App>()
            .UsePlatformDetect()
            .SetupWithoutStarting();
        return Assert.IsType<PersonalAI.Desktop.Avalonia.App>(Application.Current);
    }

    private static void AssertTheme(
        SettingsView view,
        Application application,
        SettingsViewModel viewModel,
        ThemePreference expected)
    {
        Assert.Equal(expected, viewModel.Theme);
        Assert.True(ThemeButton(view, expected).IsChecked);
        Assert.All(
            Enum.GetValues<ThemePreference>().Where(theme => theme != expected),
            theme => Assert.False(ThemeButton(view, theme).IsChecked));

        Assert.True(application.TryGetResource(
            "AccentBrush",
            application.ActualThemeVariant,
            out var resource));
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(resource);
        Assert.Equal(
            Color.Parse(AvaloniaThemeManager.GetPalette(expected).Accent),
            brush.Color);
    }

    private static void AssertCategory(
        SettingsView view,
        string expected,
        bool compact)
    {
        Assert.True(CategoryButton(view, expected, medium: false).IsChecked);
        Assert.True(CategoryButton(view, expected, medium: true).IsChecked);

        foreach (var category in Categories)
        {
            var selected = category == expected;
            Assert.Equal(
                compact || selected,
                view.FindControl<Control>(category + "Panel")!.IsVisible);
            Assert.Equal(
                selected,
                CategoryButton(view, category, medium: false).IsChecked);
            Assert.Equal(
                selected,
                CategoryButton(view, category, medium: true).IsChecked);
        }
    }

    private static RadioButton ThemeButton(SettingsView view, ThemePreference theme) =>
        view.GetLogicalDescendants()
            .OfType<RadioButton>()
            .Single(button =>
                button.GroupName == "AedaPalette" &&
                Equals(button.Tag, theme.ToString()));

    private static RadioButton CategoryButton(
        SettingsView view,
        string category,
        bool medium) =>
        view.GetLogicalDescendants()
            .OfType<RadioButton>()
            .Single(button =>
                button.GroupName == (medium
                    ? "SettingsMediumCategories"
                    : "SettingsWideCategories") &&
                Equals(button.Tag, category));

    private static void SelectNatively(RadioButton button) =>
        ((ISelectionItemProvider)new RadioButtonAutomationPeer(button)).Select();

    private static void ToggleThroughClickPath(RadioButton button) =>
        ((IToggleProvider)new RadioButtonAutomationPeer(button)).Toggle();

    private static SettingsViewModel CreateViewModel(IApplicationSettingsService settings) =>
        new(
            settings,
            new StartupRegistrationService(),
            _ => Task.FromResult(new SettingsApplyResult(true, "ok")),
            _ => { },
            () => { },
            _ => Task.FromResult<IReadOnlyList<string>>([]),
            new WorkspaceManagementViewModel(
                new WorkspaceRegistrationService(),
                new FolderPickerService()));

    private static ApplicationSettings Settings(ThemePreference theme)
    {
        var settings = ApplicationSettings.CreateDefault();
        return settings with
        {
            Appearance = settings.Appearance with { Theme = theme }
        };
    }

    private static async Task RunProbeInIsolatedProcessAsync(
        string testName,
        string probe)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(typeof(AvaloniaSettingsSelectionBehaviorTests).Assembly.Location);
        startInfo.ArgumentList.Add(
            "--TestCaseFilter:FullyQualifiedName=" +
            typeof(AvaloniaSettingsSelectionBehaviorTests).FullName + "." + testName);
        startInfo.Environment[ProbeVariable] = probe;

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var output = await standardOutput + await standardError;

        Assert.True(process.ExitCode == 0, output);
    }

    private static readonly string[] Categories =
    [
        "Appearance", "Assist", "Window", "Provider", "Privacy", "Workspaces", "Advanced"
    ];

    private sealed class RecordingSettingsService(ApplicationSettings settings)
        : IApplicationSettingsService
    {
        public ApplicationSettings Current { get; private set; } = settings;
        public string SettingsPath => "settings.json";
        public int SaveCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveAsync(
            ApplicationSettings updated,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            Current = updated;
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StartupRegistrationService : IStartupRegistrationService
    {
        public bool IsSupported => false;
        public bool IsEnabled() => false;
        public StartupRegistrationResult SetEnabled(bool enabled) =>
            new(false, "Unavailable");
    }

    private sealed class FolderPickerService : IFolderPickerService
    {
        public Task<string?> PickSingleFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class WorkspaceRegistrationService : IWorkspaceRegistrationService
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<IReadOnlyList<PersistedWorkspace>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PersistedWorkspace>>([]);
        public Task<PersistedWorkspace> RegisterAsync(
            string rootPath,
            string displayName,
            string source,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task RemoveAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PersistedWorkspace?> UpdateDisplayNameAsync(
            WorkspaceId workspaceId,
            string displayName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PersistedWorkspace?> RevalidateAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task RevalidateAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
