using Avalonia.Controls;
using PersonalAI.Core.Permissions;
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;
using PersonalAI.Desktop.Avalonia.Themes;
using PersonalAI.Desktop.Avalonia.Views.Settings;
using PersonalAI.Desktop.Avalonia.Views.Tasks;
using PersonalAI.Desktop.Presentation.Services;
using PersonalAI.Desktop.Presentation.ViewModels;
using PersonalAI.Infrastructure.Hosting;

namespace PersonalAI.Desktop.Avalonia.Composition;

public sealed class AvaloniaAppComposition : IAsyncDisposable
{
    private readonly AvaloniaThemeManager _themeManager;

    private AvaloniaAppComposition(AedaRuntime runtime, Action<Action> dispatch)
    {
        Runtime = runtime;
        Chat = new AvaloniaChatViewModel(runtime.ConversationSession, runtime.Settings, dispatch);
        _themeManager = new AvaloniaThemeManager(runtime.Settings.Current.Appearance.Theme);
        SettingsView? settingsView = null;
        var folderPicker = new AvaloniaFolderPickerService(() =>
            settingsView is null ? null : TopLevel.GetTopLevel(settingsView));
        var workspaces = new WorkspaceManagementViewModel(
            runtime.WorkspaceRegistration,
            folderPicker);
        var settings = new SettingsViewModel(
            runtime.Settings,
            new DeferredStartupRegistrationService(),
            _ => Task.FromResult(new SettingsApplyResult(
                false,
                "Hotkey changes become available with Avalonia shell integration.")),
            value => _themeManager.Apply(value.Appearance.Theme),
            () => { },
            runtime.ListCurrentModelsAsync,
            workspaces);
        Screens =
        [
            new AvaloniaPresentationScreen(
                "aeda-task-center",
                "Task Center",
                new AedaTaskCenterViewModel(runtime.TaskCenter),
                () => new TaskCenterView(),
                content => ((TaskCenterView)content).FocusPrimaryAction()),
            new AvaloniaPresentationScreen(
                "settings",
                "Settings",
                settings,
                () => settingsView = new SettingsView(_themeManager, runtime.Settings),
                content => ((SettingsView)content).FocusPrimaryAction())
        ];
    }

    public AedaRuntime Runtime { get; }

    public AvaloniaChatViewModel Chat { get; }

    public IReadOnlyList<AvaloniaPresentationScreen> Screens { get; }

    public static async Task<AvaloniaAppComposition> CreateAsync(Action<Action> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        var runtime = await AedaRuntime.CreateAsync(new DenyingPermissionBroker());
        return new AvaloniaAppComposition(runtime, dispatch);
    }

    public async ValueTask DisposeAsync()
    {
        Chat.Dispose();
        _themeManager.Dispose();
        await Runtime.DisposeAsync();
    }
}
