using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Desktop.WinUI.ViewModels;
using PersonalAI.Desktop.WinUI.Views;
using PersonalAI.Infrastructure.Context;
using PersonalAI.Infrastructure.Hosting;
using PersonalAI.Infrastructure.Ipc;
using PersonalAI.Infrastructure.Modules;

namespace PersonalAI.Desktop.WinUI;

public partial class App : Application
{
    private Window? _window;
    private MainWindow? _mainWindow;
    private AssistPillWindow? _assistPillWindow;
    private AssistPillViewModel? _assistPillViewModel;
    private MainViewModel? _viewModel;
    private IApplicationSettingsService? _settingsService;
    private IStartupRegistrationService? _startupRegistrationService;
    private WinUiSingleInstanceService? _singleInstanceService;
    private WinUiTrayIconService? _trayIconService;
    private WinUiGlobalHotKeyService? _hotKeyService;
    private WinUiWindowActivationService? _activationService;
    private WinUiWindowPlacementService? _placementService;
    private WinUiWindowPlacementService? _assistPillPlacementService;
    private ForegroundWindowTracker? _foregroundWindowTracker;
    private ExternalForegroundWindowMonitor? _foregroundMonitor;
    private PersonalAiPipeServer? _pipeServer;
    private WinUiPermissionBroker? _permissionBroker;
    private AedaRuntime? _runtime;
    private TaskTimelineViewModel? _taskTimeline;
    private bool _isExiting;
    private bool _isWindowVisible;
    private bool _assistPillReady;
    private bool _shellResourcesDisposed;

    public App()
    {
        AedaWindowChrome.InitializeProcessIdentity();
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

        _permissionBroker = new WinUiPermissionBroker(
            DispatcherQueue.GetForCurrentThread(),
            () => _mainWindow?.ApprovalXamlRoot);
        _runtime = await AedaRuntime.CreateAsync(_permissionBroker);
        _settingsService = _runtime.Settings;
        AedaThemeManager.Apply(_settingsService.Current.Appearance.Theme);
        _startupRegistrationService = new WindowsStartupRegistrationService();
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
            () => _settingsService.Current.Privacy,
            new UniversalSelectedTextService(
                new WindowsUiaSelectedTextProvider(),
                new WindowsClipboardCopySelectedTextProvider(GetAssistPillWindowHandle)),
            () => _settingsService.Current.Context.MaxIndividualClipboardCharacters,
            () => _settingsService.Current.AssistPill.UniversalSelectionFallbackEnabled);
        var screenshotAttachmentService = new ScreenshotAttachmentService(
            new ScreenshotContextService(
                activeContextProvider,
                _foregroundWindowTracker,
                GetWindowHandle,
                () => _settingsService.Current.Context));
        var aedaCodeViewModel = new AedaCodeModuleViewModel(
            _runtime.CodeModule,
            _runtime.ModuleRegistry,
            _runtime.WorkspaceRegistry,
            _runtime.TaskCenter,
            _runtime.ApprovalCheckpointStore);
        var aedaMemoryViewModel = new AedaMemoryModuleViewModel(
            _runtime.MemoryModule,
            _runtime.ModuleRegistry);
        var aedaResearchViewModel = new AedaResearchModuleViewModel(
            _runtime.ResearchModule,
            _runtime.ModuleRegistry);
        var workspaceManagement = new WorkspaceManagementViewModel(
            _runtime.WorkspaceRegistration,
            new WinUiFolderPickerService(
                () => _mainWindow?.AppWindow.Id));
        await workspaceManagement.RefreshAsync();
        _taskTimeline = new TaskTimelineViewModel(
            _runtime.TaskEventBus,
            DispatcherQueue.GetForCurrentThread());
        var settingsViewModel = new SettingsViewModel(
            _settingsService,
            _startupRegistrationService,
            ApplyHotkeyAsync,
            ApplyRuntimeSettings,
            () =>
            {
                _placementService?.ResetRememberedPosition();
                _assistPillPlacementService?.ResetRememberedPosition();
            },
            _runtime.ListCurrentModelsAsync,
            workspaceManagement);
        var clipboardWriter = new WinUiClipboardWriter();
        var viewModel = new MainViewModel(
            _runtime.ConversationSession,
            clipboardContextService,
            activeWindowContextService,
            screenshotAttachmentService,
            _settingsService,
            settingsViewModel,
            new PersonalAI.Core.Chat.DeterministicChatModelRouter(),
            _runtime.ToolRuntime,
            _runtime.ModuleRegistry,
            new ModuleSuggestionService(),
            new AedaModuleDashboardViewModel(
                _runtime.ModuleRegistry,
                _runtime.TaskQueryService,
                _runtime.WorkspaceRegistry,
                descriptor => _viewModel?.OpenModule(descriptor)),
            new AedaTaskCenterViewModel(_runtime.TaskCenter),
            aedaCodeViewModel,
            aedaMemoryViewModel,
            aedaResearchViewModel,
            _taskTimeline,
            _runtime.WorkspaceRegistry,
            clipboardWriter,
            _runtime.ListCurrentModelsAsync);
        _viewModel = viewModel;
        await viewModel.InitializeAsync();
        _ = settingsViewModel.RefreshModelsAsync();

        _mainWindow = new MainWindow(viewModel);
        _window = _mainWindow;
        _activationService = new WinUiWindowActivationService(_window);
        _placementService = new WinUiWindowPlacementService();
        _placementService.ConfigureWindow(_window);
        _assistPillPlacementService = new WinUiWindowPlacementService(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "assist-pill-position-v2.json"),
            AssistPillWindow.IdleWidth,
            AssistPillWindow.IdleHeight,
            AssistPillWindow.IdleWidth,
            AssistPillWindow.IdleHeight);
        _assistPillViewModel = new AssistPillViewModel(
            new AssistPillHost(
                _runtime.ConversationSession,
                _settingsService,
                new PersonalAI.Core.Chat.DeterministicChatModelRouter(),
                _runtime.CheckCurrentProviderAsync,
                _runtime.ListCurrentModelsAsync,
                activeWindowContextService,
                new ScreenTextCaptureService(),
                () => viewModel.AttachedContexts.LastOrDefault(context =>
                    _foregroundWindowTracker.IsLastObservedExternalWindowSafe &&
                    AssistContextPolicy.IsMeaningful(context, DateTimeOffset.UtcNow) &&
                    AssistContextPolicy.MatchesForeground(
                        context,
                        _foregroundWindowTracker.GetLastValidExternalWindow())),
                clipboardWriter,
                async conversationId =>
                {
                    ShowPersonalAi(repositionIfHidden: true);
                    if (conversationId is { } id)
                    {
                        await viewModel.OpenConversationAsync(id);
                    }
                }),
            _settingsService.Current.AssistPill);
        _assistPillWindow = new AssistPillWindow(
            _assistPillViewModel,
            _assistPillPlacementService,
            _foregroundWindowTracker);
        ApplyRuntimeSettings(_settingsService.Current);
        _window.AppWindow.Closing += MainWindow_Closing;

        StartEditorIpc(
            viewModel,
            DispatcherQueue.GetForCurrentThread(),
            _runtime.EditorResponder);
        StartForegroundTracking();
        StartTrayIcon();
        RegisterHotKey();
        StartBackgroundExperience();
    }

    private void StartBackgroundExperience()
    {
        _assistPillReady = true;
        _assistPillWindow?.ShowIdle();
        _isWindowVisible = false;
        _viewModel?.SetEditorConnectionState(
            _viewModel.IsEditorConnected,
            "AEDA is running in the background.");
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
            if (_settingsService?.Current.AssistPill.Enabled == true)
            {
                _ = ToggleAssistPillAsync();
            }
            else
            {
                ShowPersonalAi(repositionIfHidden: true);
            }
        };

        if (!_hotKeyService.Register())
        {
            _viewModel?.SetEditorConnectionState(
                _viewModel.IsEditorConnected,
                $"{HotkeySettingsValidator.Format(settings.Hotkey)} is unavailable; another app may own it.");
        }
    }

    private async Task ToggleAssistPillAsync()
    {
        try
        {
            if (_assistPillWindow is not null)
            {
                await _assistPillWindow.ToggleAsync();
            }
        }
        catch
        {
            _assistPillWindow?.ShowIdle();
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
        var pillWasEnabled = _assistPillViewModel?.IsEnabled == true;
        if (_placementService is not null)
        {
            _placementService.RememberWindowPosition =
                settings.Window.RememberWindowPosition;
        }

        if (_assistPillPlacementService is not null)
        {
            _assistPillPlacementService.RememberWindowPosition =
                settings.Window.RememberWindowPosition;
        }

        _viewModel?.ApplySettings(settings);
        _assistPillViewModel?.ApplySettings(settings.AssistPill);
        if (_assistPillReady && !pillWasEnabled && settings.AssistPill.Enabled)
        {
            _assistPillWindow?.ShowIdle();
        }

        var elementTheme = AedaThemeManager.Apply(settings.Appearance.Theme);
        _viewModel?.ModuleDashboard.RefreshTheme();
        _mainWindow?.ApplyTheme(elementTheme);
        _assistPillWindow?.ApplyTheme(elementTheme);
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
        DispatcherQueue dispatcherQueue,
        EditorCodeChatResponder editorCodeResponder)
    {
        var handler = new EditorContextMessageHandler(
            envelope => dispatcherQueue.TryEnqueue(() =>
            {
                if (!envelope.Command.Equals(
                        PersonalAI.Core.Editor.EditorContextCommands.UpdateSelectionContext,
                        StringComparison.OrdinalIgnoreCase))
                {
                    ShowPersonalAi(repositionIfHidden: true);
                }
                viewModel.ReceiveEditorContext(envelope);
            }),
            () => dispatcherQueue.TryEnqueue(
                () => ShowPersonalAi(repositionIfHidden: true)),
            editorCodeResponder.RespondAsync);

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

    private nint GetAssistPillWindowHandle() => _assistPillWindow is null
        ? GetWindowHandle()
        : WinRT.Interop.WindowNative.GetWindowHandle(_assistPillWindow);

    private void DisposeShellResources()
    {
        if (_shellResourcesDisposed)
        {
            return;
        }

        _shellResourcesDisposed = true;
        _pipeServer?.Dispose();
        _pipeServer = null;
        _assistPillWindow?.Close();
        _assistPillWindow = null;
        _assistPillViewModel = null;
        _foregroundMonitor?.Dispose();
        _foregroundMonitor = null;
        _hotKeyService?.Dispose();
        _hotKeyService = null;
        _trayIconService?.Dispose();
        _trayIconService = null;
        _taskTimeline?.Dispose();
        _taskTimeline = null;
        _runtime?.DisposeAsync().GetAwaiter().GetResult();
        _runtime = null;
        _permissionBroker?.Dispose();
        _permissionBroker = null;
        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
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
