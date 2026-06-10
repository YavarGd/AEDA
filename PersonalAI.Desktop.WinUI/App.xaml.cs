using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Desktop.WinUI.ViewModels;
using PersonalAI.Desktop.WinUI.Views;
using PersonalAI.Infrastructure.Chat;
using PersonalAI.Infrastructure.Context;
using PersonalAI.Infrastructure.Ipc;

namespace PersonalAI.Desktop.WinUI;

public partial class App : Application
{
    private const uint VirtualKeySpace = 0x20;
    private const uint HotKeyModifiersControlAlt = 0x0001 | 0x0002;

    private Window? _window;
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;
    private WinUiSingleInstanceService? _singleInstanceService;
    private WinUiTrayIconService? _trayIconService;
    private WinUiGlobalHotKeyService? _hotKeyService;
    private WinUiWindowActivationService? _activationService;
    private WinUiWindowPlacementService? _placementService;
    private ForegroundWindowTracker? _foregroundWindowTracker;
    private ExternalForegroundWindowMonitor? _foregroundMonitor;
    private PersonalAiPipeServer? _pipeServer;
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
        var conversationRepository = ConversationRepositoryFactory.CreateDefaultRepository();
        await conversationRepository.InitializeAsync();
        var chatSession = new ChatSessionService(chatProvider);
        var conversationSession = new ConversationSessionService(
            conversationRepository,
            chatSession);
        var activeContextProvider =
            ActiveContextProviderFactory.CreateDefaultProvider();
        _foregroundWindowTracker = new ForegroundWindowTracker();
        var clipboardContextService = new ClipboardContextService();
        var activeWindowContextService = new ActiveWindowContextService(
            activeContextProvider,
            _foregroundWindowTracker,
            GetWindowHandle);
        var screenshotAttachmentService = new ScreenshotAttachmentService(
            new ScreenshotContextService(
                activeContextProvider,
                _foregroundWindowTracker,
                GetWindowHandle));
        var viewModel = new MainViewModel(
            conversationSession,
            clipboardContextService,
            activeWindowContextService,
            screenshotAttachmentService);
        _viewModel = viewModel;
        await viewModel.InitializeAsync();

        _mainWindow = new MainWindow(viewModel);
        _window = _mainWindow;
        _activationService = new WinUiWindowActivationService(_window);
        _placementService = new WinUiWindowPlacementService();
        _placementService.ConfigureWindow(_window);
        _window.AppWindow.Closing += MainWindow_Closing;

        StartEditorIpc(viewModel, DispatcherQueue.GetForCurrentThread());
        StartForegroundTracking();
        StartTrayIcon();
        RegisterHotKey();
        ShowPersonalAi(repositionIfHidden: true);
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
        _hotKeyService = new WinUiGlobalHotKeyService(
            id: 1,
            modifiers: HotKeyModifiersControlAlt,
            virtualKey: VirtualKeySpace);
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
                "Ctrl+Alt+Space is unavailable; another app may own it.");
        }
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
        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
    }

    private static class NativeMessageBox
    {
        public static void Show(string text, string caption)
        {
            MessageBox(0, text, caption, 0x00000040);
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
