using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using PersonalAI.Desktop.Avalonia.Platform.Windows;
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;
using PersonalAI.Desktop.Avalonia.Themes;
using PersonalAI.Desktop.Avalonia.Views.Assist;
using PersonalAI.Desktop.Avalonia.Views.Capture;
using PersonalAI.Desktop.Avalonia.Views.Code;
using PersonalAI.Desktop.Avalonia.Views.Memory;
using PersonalAI.Desktop.Avalonia.Views.Research;
using PersonalAI.Desktop.Avalonia.Views.Settings;
using PersonalAI.Desktop.Avalonia.Views.Tasks;
using PersonalAI.Desktop.Presentation.Services;
using PersonalAI.Desktop.Presentation.ViewModels;
using PersonalAI.Infrastructure.Hosting;

namespace PersonalAI.Desktop.Avalonia.Composition;

public sealed class AvaloniaAppComposition : IAsyncDisposable
{
    private readonly AvaloniaThemeManager _themeManager;
    private readonly AvaloniaPermissionBroker _permissionBroker;

    private AvaloniaAppComposition(
        AedaRuntime runtime,
        Action<Action> dispatch,
        AvaloniaPermissionBroker permissionBroker)
    {
        Runtime = runtime;
        _permissionBroker = permissionBroker;
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

        // The view is created lazily by the screen factory, which the shell invokes on the
        // UI thread. Avalonia controls must not be constructed here, because composition
        // runs before the dispatcher hand-off.
        AssistView? assistView = null;
        var assistPillHost = new AssistPillHost(
            runtime.ConversationSession,
            runtime.Settings,
            new PersonalAI.Core.Chat.DeterministicChatModelRouter(),
            runtime.CheckCurrentProviderAsync,
            runtime.ListCurrentModelsAsync,
            new AvaloniaAssistContextService(),
            new AvaloniaScreenTextCaptureService(
                () => TopLevel.GetTopLevel(assistView)?.Screens?.All ?? []),
            () => null,
            new AvaloniaClipboardWriter(() =>
                assistView is null ? null : TopLevel.GetTopLevel(assistView)),
            conversationId => conversationId is { } id
                ? Chat.OpenConversationAsync(id)
                : Task.CompletedTask);
        var assistPillViewModel = new AssistPillViewModel(
            assistPillHost,
            runtime.Settings.Current.AssistPill);

        Screens =
        [
            new AvaloniaPresentationScreen(
                "aeda-task-center",
                "Task Center",
                new AedaTaskCenterViewModel(runtime.TaskCenter),
                () => new TaskCenterView(),
                content => ((TaskCenterView)content).FocusPrimaryAction()),
            new AvaloniaPresentationScreen(
                "aeda-code",
                "Code",
                new AedaCodeModuleViewModel(
                    runtime.CodeModule,
                    runtime.ModuleRegistry,
                    runtime.WorkspaceRegistry,
                    runtime.TaskCenter,
                    runtime.ApprovalCheckpointStore),
                () => new CodeView(),
                content => ((CodeView)content).FocusPrimaryAction()),
            new AvaloniaPresentationScreen(
                "settings",
                "Settings",
                settings,
                () => settingsView = new SettingsView(_themeManager, runtime.Settings),
                content => ((SettingsView)content).FocusPrimaryAction()),
            new AvaloniaPresentationScreen(
                "aeda-memory",
                "Memory",
                new AedaMemoryModuleViewModel(runtime.MemoryModule, runtime.ModuleRegistry),
                () => new MemoryView(),
                content => ((MemoryView)content).FocusPrimaryAction()),
            new AvaloniaPresentationScreen(
                "aeda-research",
                "Research",
                new AedaResearchModuleViewModel(runtime.ResearchModule, runtime.ModuleRegistry),
                () => new ResearchView(),
                content => ((ResearchView)content).FocusPrimaryAction()),
            new AvaloniaPresentationScreen(
                "aeda-assist",
                "Assist",
                assistPillViewModel,
                () => assistView ??= new AssistView(),
                content => ((AssistView)content).FocusPrimaryAction())
        ];
    }

    public AedaRuntime Runtime { get; }

    public AvaloniaChatViewModel Chat { get; }

    public IReadOnlyList<AvaloniaPresentationScreen> Screens { get; }

    public static async Task<AvaloniaAppComposition> CreateAsync(Action<Action> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        var permissionBroker = new AvaloniaPermissionBroker(() =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);
        try
        {
            var runtime = await AedaRuntime.CreateAsync(permissionBroker);
            return new AvaloniaAppComposition(runtime, dispatch, permissionBroker);
        }
        catch
        {
            permissionBroker.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Chat.Dispose();
        _themeManager.Dispose();
        _permissionBroker.Dispose();
        await Runtime.DisposeAsync();
    }
}
