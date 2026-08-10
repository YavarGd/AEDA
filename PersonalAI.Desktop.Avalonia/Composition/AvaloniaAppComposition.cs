using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using PersonalAI.Desktop.Avalonia.Platform.Windows;
using PersonalAI.Desktop.Avalonia.Platform.Windows.Assist;
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
using PersonalAI.Core.Settings;
using PersonalAI.Infrastructure.Context;
using PersonalAI.Infrastructure.Hosting;
using PersonalAI.Infrastructure.Windows;

namespace PersonalAI.Desktop.Avalonia.Composition;

public sealed class AvaloniaAppComposition : IAsyncDisposable
{
    private readonly AvaloniaThemeManager _themeManager;
    private readonly AvaloniaPermissionBroker _permissionBroker;
    private readonly ForegroundWindowTracker _foregroundWindowTracker;
    private readonly ExternalForegroundWindowMonitor _foregroundWindowMonitor;
    private readonly WindowsUiaSelectedTextProvider _uiaSelectedTextProvider;
    private AvaloniaAssistWindow? _assistWindow;
    private nint _assistWindowHandle;

    private AvaloniaAppComposition(
        AedaRuntime runtime,
        Action<Action> dispatch,
        AvaloniaPermissionBroker permissionBroker)
    {
        Runtime = runtime;
        _permissionBroker = permissionBroker;
        Chat = new AvaloniaChatViewModel(runtime.ConversationSession, runtime.Settings, dispatch);
        _themeManager = new AvaloniaThemeManager(runtime.Settings.Current.Appearance.Theme);
        nint GetMainWindowHandle()
        {
            var mainWindow = (Application.Current?.ApplicationLifetime as
                IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            return mainWindow is not null &&
                AvaloniaWindowsProcessIdentity.TryGetWindowIdentity(mainWindow, out var identity)
                    ? identity.WindowHandle
                    : 0;
        }

        nint GetAedaWindowHandle() => _assistWindowHandle != 0
            ? _assistWindowHandle
            : GetMainWindowHandle();

        _foregroundWindowTracker = new ForegroundWindowTracker(
            () => runtime.Settings.Current.Privacy);
        _foregroundWindowMonitor = new ExternalForegroundWindowMonitor(
            _foregroundWindowTracker,
            GetAedaWindowHandle);
        _uiaSelectedTextProvider = new WindowsUiaSelectedTextProvider();
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
            new AvaloniaAssistContextService(
                ActiveContextProviderFactory.CreateDefaultProvider(),
                _foregroundWindowTracker,
                GetAedaWindowHandle,
                runtime.Settings,
                new UniversalSelectedTextService(
                    _uiaSelectedTextProvider,
                    new WindowsClipboardCopySelectedTextProvider(GetAedaWindowHandle))),
            new AvaloniaScreenTextCaptureService(
                () => TopLevel.GetTopLevel(assistView)?.Screens?.All ?? []),
            () => null,
            new AvaloniaClipboardWriter(() =>
                (_assistWindow is { IsVisible: true } ? _assistWindow : null) ??
                (assistView is null ? null : TopLevel.GetTopLevel(assistView)) ??
                (Application.Current?.ApplicationLifetime as
                    IClassicDesktopStyleApplicationLifetime)?.MainWindow),
            OpenAssistConversationInShellAsync);
        Assist = new AssistPillViewModel(
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
                Assist,
                () => assistView ??= new AssistView(),
                content => ((AssistView)content).FocusPrimaryAction())
        ];
        _foregroundWindowMonitor.Start();
    }

    public AedaRuntime Runtime { get; }

    public AvaloniaChatViewModel Chat { get; }

    public AssistPillViewModel Assist { get; }

    /// <summary>
    /// Surfaces an Assist-generated conversation in the shell: restore and activate the main
    /// window, then route to General chat. Set by the app root, which owns the windows.
    /// <para>
    /// This is deliberately specific to "Open in AEDA". Reacting to every
    /// <c>ActiveConversation</c> change would steal focus whenever an ordinary conversation
    /// is selected.
    /// </para>
    /// </summary>
    public Action<Guid>? SurfaceAssistConversationInShell { get; set; }

    /// <summary>
    /// Loads the conversation into the chat view model, then asks the shell to surface it.
    /// A null id (no conversation was generated) loads nothing and surfaces nothing.
    /// </summary>
    private async Task OpenAssistConversationInShellAsync(Guid? conversationId)
    {
        if (conversationId is not { } id)
        {
            return;
        }

        await Chat.OpenConversationAsync(id);
        SurfaceAssistConversationInShell?.Invoke(id);
    }

    public HotkeySettings CurrentHotkey => Runtime.Settings.Current.Hotkey;

    public bool StartMinimizedToTray => Runtime.Settings.Current.Window.StartMinimizedToTray;

    public bool ExitOnMainWindowClose =>
        Runtime.Settings.Current.Window.CloseBehavior == CloseBehavior.Exit;

    public bool AskBeforeMainWindowExit =>
        Runtime.Settings.Current.Window.CloseBehavior == CloseBehavior.AskEachTime;

    public IReadOnlyList<AvaloniaPresentationScreen> Screens { get; }

    public AvaloniaAssistWindow CreateAssistWindow()
    {
        var window = new AvaloniaAssistWindow(Assist, _foregroundWindowTracker);
        _assistWindow = window;
        window.Opened += (_, _) => _assistWindowHandle =
            AvaloniaWindowsProcessIdentity.TryGetWindowIdentity(window, out var identity)
                ? identity.WindowHandle
                : 0;
        window.Closed += (_, _) =>
        {
            _assistWindowHandle = 0;
            if (ReferenceEquals(_assistWindow, window))
            {
                _assistWindow = null;
            }
        };
        return window;
    }

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
        try
        {
            await Chat.DisposeAsync();
            await Assist.DisposeAsync();
            await _foregroundWindowMonitor.DisposeAsync();
            await _uiaSelectedTextProvider.DisposeAsync();
        }
        finally
        {
            _themeManager.Dispose();
            _permissionBroker.Dispose();
            await Runtime.DisposeAsync();
        }
    }
}
