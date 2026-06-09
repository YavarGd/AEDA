using System.Windows;
using System.Windows.Interop;
using PersonalAI.Infrastructure.Chat;
using PersonalAI.Desktop.Windows;

namespace PersonalAI.Desktop;

public partial class App : System.Windows.Application
{
    private const uint VirtualKeySpace = 0x20;
    private SingleInstanceService? _singleInstanceService;
    private ExistingInstanceNotificationService? _existingInstanceNotificationService;
    private TrayIconService? _trayIconService;
    private GlobalHotKeyService? _hotKeyService;
    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        _singleInstanceService = new SingleInstanceService();

        if (!_singleInstanceService.IsPrimaryInstance)
        {
            if (!ExistingInstanceNotificationService.NotifyExistingInstance())
            {
                System.Windows.MessageBox.Show(
                    "PersonalAI is already running. Use Ctrl+Alt+Space or the tray icon to open it.",
                    "PersonalAI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        var chatProvider = ChatProviderFactory.CreateDefaultLocalProvider();
        var conversationRepository =
            ConversationRepositoryFactory.CreateDefaultRepository();

        try
        {
            await conversationRepository.InitializeAsync();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"PersonalAI could not initialize the local conversation database. {exception.Message}",
                "PersonalAI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
            return;
        }

        _mainWindow = new MainWindow(chatProvider, conversationRepository);
        MainWindow = _mainWindow;

        _trayIconService = new TrayIconService(
            ShowPersonalAi,
            HidePersonalAi,
            ExitPersonalAi);

        _mainWindow.ShowPaletteAndFocusPrompt();
        var windowHandle = new WindowInteropHelper(_mainWindow).EnsureHandle();
        _existingInstanceNotificationService =
            new ExistingInstanceNotificationService(windowHandle);
        _existingInstanceNotificationService.ShowPaletteRequested +=
            (_, _) => ShowPersonalAi();
        RegisterHotKey(windowHandle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotKeyService?.Dispose();
        _existingInstanceNotificationService?.Dispose();
        _trayIconService?.Dispose();
        _singleInstanceService?.Dispose();

        base.OnExit(e);
    }

    private void RegisterHotKey(nint windowHandle)
    {
        _hotKeyService = new GlobalHotKeyService(
            windowHandle,
            new GlobalHotKey(
                Id: 1,
                Modifiers: HotKeyModifiers.Control | HotKeyModifiers.Alt,
                VirtualKey: VirtualKeySpace));
        _hotKeyService.HotKeyPressed += (_, _) => ShowPersonalAi();

        if (!_hotKeyService.Register())
        {
            System.Windows.MessageBox.Show(
                "PersonalAI could not register Ctrl+Alt+Space. Another application may already be using that shortcut.",
                "PersonalAI",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowPersonalAi()
    {
        _mainWindow?.ShowPaletteAndFocusPrompt();
    }

    private void HidePersonalAi()
    {
        _mainWindow?.HidePalette();
    }

    private void ExitPersonalAi()
    {
        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
        }

        Shutdown();
    }
}
