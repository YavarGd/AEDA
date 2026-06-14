using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using PersonalAI.Core.Settings;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Reference;
using PersonalAI.Core.Workspaces;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Desktop.WinUI.ViewModels;
using PersonalAI.Desktop.WinUI.Views;
using PersonalAI.Infrastructure.Chat;
using PersonalAI.Infrastructure.Context;
using PersonalAI.Infrastructure.Ipc;
using PersonalAI.Infrastructure.Settings;
using PersonalAI.Infrastructure.Tools;
using PersonalAI.Infrastructure.Tools.Workspace;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Desktop.WinUI;

public partial class App : Application
{
    private Window? _window;
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;
    private IApplicationSettingsService? _settingsService;
    private IStartupRegistrationService? _startupRegistrationService;
    private WinUiSingleInstanceService? _singleInstanceService;
    private WinUiTrayIconService? _trayIconService;
    private WinUiGlobalHotKeyService? _hotKeyService;
    private WinUiWindowActivationService? _activationService;
    private WinUiWindowPlacementService? _placementService;
    private ForegroundWindowTracker? _foregroundWindowTracker;
    private ExternalForegroundWindowMonitor? _foregroundMonitor;
    private PersonalAiPipeServer? _pipeServer;
    private WinUiPermissionBroker? _permissionBroker;
    private TaskTimelineViewModel? _taskTimeline;
    private bool _isExiting;
    private bool _isWindowVisible;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstanceService = new WinUiSingleInstanceService();

        if (!_singleInstanceService.IsPrimaryInstance)
        {
            NativeMessageBox.Show(
                "PersonalAI WinUI is already running.",
                "PersonalAI");
            Exit();
            return;
        }

        var chatProvider = ChatProviderFactory.CreateDefaultLocalProvider();
        var modelCatalog = chatProvider as PersonalAI.Core.Chat.IChatModelCatalog;
        var conversationRepository = ConversationRepositoryFactory.CreateDefaultRepository();
        await conversationRepository.InitializeAsync();
        _settingsService = new JsonApplicationSettingsService();
        await _settingsService.InitializeAsync();
        _startupRegistrationService = new WindowsStartupRegistrationService();
        var chatSession = new ChatSessionService(chatProvider);
        var activeContextProvider =
            ActiveContextProviderFactory.CreateDefaultProvider();
        _foregroundWindowTracker = new ForegroundWindowTracker(
            () => _settingsService.Current.Privacy);
        var clipboardContextService = new ClipboardContextService(
            () => _settingsService.Current.Context);
        var activeWindowContextService = new ActiveWindowContextService(
            activeContextProvider,
            _foregroundWindowTracker,
            GetWindowHandle,
            () => _settingsService.Current.Privacy);
        var screenshotAttachmentService = new ScreenshotAttachmentService(
            new ScreenshotContextService(
                activeContextProvider,
                _foregroundWindowTracker,
                GetWindowHandle,
                () => _settingsService.Current.Context));
        var taskEventBus = new TaskEventBus();
        var toolRegistry = new TypedToolRegistry();
        toolRegistry.Register(new GetCurrentUtcTimeTool());
        IWorkspaceRegistry workspaceRegistry = new WorkspaceRegistry();
        var workspaceOptions = new WorkspaceToolOptions();
        var workspaceResolver = new WorkspacePathResolver(workspaceRegistry);
        var workspaceReader = new FileSystemWorkspaceReader(
            workspaceRegistry,
            workspaceResolver,
            workspaceOptions);
        toolRegistry.Register(new GetWorkspaceInfoTool(
            workspaceReader,
            workspaceResolver,
            workspaceOptions));
        toolRegistry.Register(new ListDirectoryTool(
            workspaceReader,
            workspaceResolver,
            workspaceOptions));
        toolRegistry.Register(new ReadTextFileTool(
            workspaceReader,
            workspaceResolver,
            workspaceOptions));
        toolRegistry.Register(new SearchWorkspaceTextTool(
            workspaceReader,
            workspaceResolver,
            workspaceOptions));
        _permissionBroker = new WinUiPermissionBroker(
            DispatcherQueue.GetForCurrentThread(),
            () => _mainWindow?.ApprovalXamlRoot);
        var toolRuntime = new TypedToolRuntime(
            toolRegistry,
            taskEventBus,
            _permissionBroker);
        var conversationSession = new ConversationSessionService(
            conversationRepository,
            chatSession,
            toolRegistry,
            toolRuntime,
            workspaceRegistry);
        var workspaceRepository = WorkspaceRepositoryFactory.CreateDefaultRepository();
        var workspaceRegistrationService = new WorkspaceRegistrationService(
            workspaceRepository,
            workspaceRegistry,
            toolRuntime);
        await workspaceRegistrationService.InitializeAsync();
        var workspaceManagement = new WorkspaceManagementViewModel(
            workspaceRegistrationService,
            new WinUiFolderPickerService(GetWindowHandle));
        await workspaceManagement.RefreshAsync();
        _taskTimeline = new TaskTimelineViewModel(
            taskEventBus,
            DispatcherQueue.GetForCurrentThread());
        var settingsViewModel = new SettingsViewModel(
            _settingsService,
            _startupRegistrationService,
            ApplyHotkeyAsync,
            ApplyRuntimeSettings,
            () => _placementService?.ResetRememberedPosition(),
            cancellationToken => modelCatalog?.ListModelsAsync(cancellationToken) ??
                Task.FromResult<IReadOnlyList<string>>([]),
            workspaceManagement);
        var viewModel = new MainViewModel(
            conversationSession,
            clipboardContextService,
            activeWindowContextService,
            screenshotAttachmentService,
            _settingsService,
            settingsViewModel,
            new PersonalAI.Core.Chat.DeterministicChatModelRouter(),
            toolRuntime,
            _taskTimeline,
            workspaceRegistry,
            cancellationToken => modelCatalog?.ListModelsAsync(cancellationToken) ??
                Task.FromResult<IReadOnlyList<string>>([]));
        _viewModel = viewModel;
        await viewModel.InitializeAsync();
        _ = settingsViewModel.RefreshModelsAsync();

        _mainWindow = new MainWindow(viewModel);
        _window = _mainWindow;
        _activationService = new WinUiWindowActivationService(_window);
        _placementService = new WinUiWindowPlacementService();
        _placementService.ConfigureWindow(_window);
        ApplyRuntimeSettings(_settingsService.Current);
        _window.AppWindow.Closing += MainWindow_Closing;

        StartEditorIpc(viewModel, DispatcherQueue.GetForCurrentThread());
        StartForegroundTracking();
        StartTrayIcon();
        RegisterHotKey();

        if (_settingsService.Current.Window.StartMinimizedToTray)
        {
            _isWindowVisible = false;
            _viewModel.SetEditorConnectionState(
                _viewModel.IsEditorConnected,
                "PersonalAI is running in the system tray.");
        }
        else
        {
            ShowPersonalAi(repositionIfHidden: true);
        }
    }

    private void StartTrayIcon()
    {
        _trayIconService = new WinUiTrayIconService(
            () => ShowPersonalAi(repositionIfHidden: true),
            NewChatFromTray,
            ExitPersonalAi);
    }

    private void RegisterHotKey()
    {
        var settings = _settingsService?.Current ?? ApplicationSettings.CreateDefault();

        if (!WinUiHotkeyMapper.TryMap(
                settings.Hotkey,
                out var hotkey,
                out var errorMessage))
        {
            _viewModel?.SetEditorConnectionState(
                _viewModel.IsEditorConnected,
                errorMessage);
            return;
        }

        _hotKeyService = new WinUiGlobalHotKeyService(
            id: 1,
            modifiers: hotkey.Modifiers,
            virtualKey: hotkey.VirtualKey);
        _hotKeyService.HotKeyPressed += (_, _) =>
        {
            _ = _foregroundWindowTracker?.CaptureCurrentExternalWindow(
                GetWindowHandle());
            ShowPersonalAi(repositionIfHidden: true);
        };

        if (!_hotKeyService.Register())
        {
            _viewModel?.SetEditorConnectionState(
                _viewModel.IsEditorConnected,
                $"{HotkeySettingsValidator.Format(settings.Hotkey)} is unavailable; another app may own it.");
        }
    }

    private async Task<SettingsApplyResult> ApplyHotkeyAsync(
        ApplicationSettings settings)
    {
        if (!WinUiHotkeyMapper.TryMap(
                settings.Hotkey,
                out var hotkey,
                out var errorMessage))
        {
            return new SettingsApplyResult(false, errorMessage);
        }

        if (_hotKeyService is null)
        {
            return new SettingsApplyResult(false, "Hotkey service is unavailable.");
        }

        await Task.Yield();
        var changed = _hotKeyService.TryChange(hotkey.Modifiers, hotkey.VirtualKey);

        return changed
            ? new SettingsApplyResult(
                true,
                $"Hotkey set to {HotkeySettingsValidator.Format(settings.Hotkey)}.")
            : new SettingsApplyResult(
                false,
                $"{HotkeySettingsValidator.Format(settings.Hotkey)} is unavailable; another app may own it.");
    }

    private void ApplyRuntimeSettings(ApplicationSettings settings)
    {
        if (_placementService is not null)
        {
            _placementService.RememberWindowPosition =
                settings.Window.RememberWindowPosition;
        }

        _viewModel?.ApplySettings(settings);
        _mainWindow?.ApplyTheme(MapTheme(settings.Appearance.Theme));
    }

    private void StartForegroundTracking()
    {
        if (_foregroundWindowTracker is null)
        {
            return;
        }

        _foregroundMonitor = new ExternalForegroundWindowMonitor(
            _foregroundWindowTracker,
            GetWindowHandle);
        _foregroundMonitor.Start();
    }

    private void StartEditorIpc(
        MainViewModel viewModel,
        DispatcherQueue dispatcherQueue)
    {
        var handler = new EditorContextMessageHandler(
            envelope => dispatcherQueue.TryEnqueue(() =>
            {
                ShowPersonalAi(repositionIfHidden: true);
                viewModel.ReceiveEditorContext(envelope);
            }),
            () => dispatcherQueue.TryEnqueue(
                () => ShowPersonalAi(repositionIfHidden: true)));

        _pipeServer = new PersonalAiPipeServer(handler);
        _pipeServer.StateChanged += (_, _) => dispatcherQueue.TryEnqueue(() =>
        {
            viewModel.SetEditorConnectionState(
                _pipeServer.State == EditorIpcConnectionState.Listening,
                _pipeServer.StatusMessage);
        });
        _pipeServer.Start();
        viewModel.SetEditorConnectionState(
            _pipeServer.State == EditorIpcConnectionState.Listening,
            _pipeServer.StatusMessage);
    }

    private void ShowPersonalAi(bool repositionIfHidden)
    {
        if (_window is null ||
            _activationService is null ||
            _placementService is null)
        {
            return;
        }

        if (!_isWindowVisible && repositionIfHidden)
        {
            _placementService.PlaceForActivation(
                _window,
                _foregroundWindowTracker?.GetLastValidExternalWindow());
        }

        _activationService.ShowRestoreAndActivate();
        _mainWindow?.FocusPromptInput();
        _isWindowVisible = true;
    }

    private void HidePersonalAi()
    {
        _activationService?.Hide();
        _isWindowVisible = false;
    }

    private void NewChatFromTray()
    {
        ShowPersonalAi(repositionIfHidden: true);
        _viewModel?.NewChatCommand.Execute(null);
    }

    private void ExitPersonalAi()
    {
        _isExiting = true;
        DisposeShellResources();
        _window?.Close();
        Exit();
    }

    private void MainWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExiting)
        {
            return;
        }

        var closeBehavior =
            _settingsService?.Current.Window.CloseBehavior ?? CloseBehavior.HideToTray;

        if (closeBehavior == CloseBehavior.Exit ||
            (closeBehavior == CloseBehavior.AskEachTime &&
            NativeMessageBox.ShowYesNo(
                "Exit PersonalAI instead of hiding it in the tray?",
                "PersonalAI")))
        {
            _isExiting = true;
            DisposeShellResources();
            Exit();
            return;
        }

        args.Cancel = true;
        HidePersonalAi();
        _viewModel?.SetEditorConnectionState(
            _viewModel.IsEditorConnected,
            "PersonalAI is hidden in the system tray.");
    }

    private nint GetWindowHandle()
    {
        return _window is null
            ? 0
            : WinRT.Interop.WindowNative.GetWindowHandle(_window);
    }

    private void DisposeShellResources()
    {
        _pipeServer?.Dispose();
        _pipeServer = null;
        _foregroundMonitor?.Dispose();
        _foregroundMonitor = null;
        _hotKeyService?.Dispose();
        _hotKeyService = null;
        _trayIconService?.Dispose();
        _trayIconService = null;
        _taskTimeline?.Dispose();
        _taskTimeline = null;
        _permissionBroker?.Dispose();
        _permissionBroker = null;
        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
    }

    private static ElementTheme MapTheme(ThemePreference theme)
    {
        return theme switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private static class NativeMessageBox
    {
        public static void Show(string text, string caption)
        {
            MessageBox(0, text, caption, 0x00000040);
        }

        public static bool ShowYesNo(string text, string caption)
        {
            const uint yesNoQuestion = 0x00000004 | 0x00000020;
            const int idYes = 6;
            return MessageBox(0, text, caption, yesNoQuestion) == idYes;
        }

        [System.Runtime.InteropServices.DllImport(
            "user32.dll",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int MessageBox(
            nint hWnd,
            string lpText,
            string lpCaption,
            uint uType);
    }
}
