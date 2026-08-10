using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PersonalAI.Core.Editor;
using PersonalAI.Desktop.Avalonia.Composition;
using PersonalAI.Desktop.Avalonia.Platform.Windows;
using PersonalAI.Desktop.Avalonia.Platform.Windows.Assist;
using PersonalAI.Desktop.Avalonia.Themes;
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;
using PersonalAI.Desktop.Avalonia.Views.Dialogs;
using PersonalAI.Infrastructure.Ipc;
using PersonalAI.Infrastructure.Windows;

namespace PersonalAI.Desktop.Avalonia;

public partial class App : Application
{
    static App() => Window.WindowOpenedEvent.AddClassHandler<Window>(
        (window, _) => (Current as App)?._textScaleManager?.Attach(window));

    private AvaloniaAppComposition? _composition;
    private MainWindow? _mainWindow;
    private AvaloniaAssistWindow? _assistWindow;
    private AvaloniaWindowActivationService? _mainWindowActivation;
    private AvaloniaTrayIconService? _trayIcon;
    private WindowsGlobalHotKeyService? _hotKey;
    private PersonalAiPipeServer? _pipeServer;
    private AvaloniaShutdownCoordinator? _shutdownCoordinator;
    private bool _isExiting;
    private bool _resourcesDisposed;
    private bool _exitConfirmationOpen;
    private AvaloniaTextScaleManager? _textScaleManager;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _shutdownCoordinator = new AvaloniaShutdownCoordinator(
            DisposeResourcesAsync,
            desktop.Shutdown);
        _textScaleManager = new AvaloniaTextScaleManager(
            new WindowsTextScaleSource(),
            action => Dispatcher.UIThread.Post(action));
        desktop.ShutdownRequested += OnShutdownRequested;
        try
        {
            var window = new MainWindow();
            _ = InitializeCompositionAsync(desktop, window);
        }
        catch
        {
            RequestExit(1);
        }
        finally
        {
            base.OnFrameworkInitializationCompleted();
        }
    }

    private async Task InitializeCompositionAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow window)
    {
        try
        {
            var composition = await AvaloniaAppComposition.CreateAsync(
                action => Dispatcher.UIThread.Post(action));
            if (_isExiting)
            {
                await composition.DisposeAsync();
                return;
            }

            _composition = composition;
            await composition.Chat.InitializeAsync();
            if (_isExiting)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
                CompleteInitialization(desktop, window));
        }
        catch
        {
            RequestExit(1);
        }
    }

    private void CompleteInitialization(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow window)
    {
        if (_isExiting)
        {
            return;
        }

        if (_composition is null)
        {
            throw new InvalidOperationException("Composition is unavailable.");
        }

        window.AttachComposition(_composition.Chat, _composition.Screens);
        desktop.MainWindow = window;
        _mainWindow = window;
        _mainWindowActivation = new AvaloniaWindowActivationService(window);
        _assistWindow = _composition.CreateAssistWindow();
        window.Closing += OnMainWindowClosing;
        _composition.SurfaceAssistConversationInShell = SurfaceAssistConversation;

        _trayIcon = new AvaloniaTrayIconService(
            ShowMainWindow,
            NewChat,
            () => RequestExit());
        RegisterHotKey(_composition);
        StartEditorIpc(_composition);
        _assistWindow.ShowIdle();

        if (!_composition.StartMinimizedToTray)
        {
            window.Show();
        }
    }

    private void RegisterHotKey(AvaloniaAppComposition composition)
    {
        if (!WindowsHotkeyMapper.TryMap(
                composition.CurrentHotkey,
                out var hotkey,
                out var errorMessage))
        {
            ReportShellStatus(errorMessage);
            return;
        }

        _hotKey = new WindowsGlobalHotKeyService(1, hotkey.Modifiers, hotkey.VirtualKey);
        _hotKey.HotKeyPressed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_composition?.Assist.IsEnabled == true)
            {
                _ = ToggleAssistAsync();
            }
            else
            {
                ShowMainWindow();
            }
        });
        if (!_hotKey.Register())
        {
            ReportShellStatus("The configured AEDA hotkey is unavailable; another app may own it.");
        }
    }

    private async Task ToggleAssistAsync()
    {
        if (_isExiting)
        {
            return;
        }

        try
        {
            if (_assistWindow is not null)
            {
                await _assistWindow.ToggleAsync();
            }
        }
        catch
        {
            _assistWindow?.ShowIdle();
        }
    }

    private void StartEditorIpc(AvaloniaAppComposition composition)
    {
        var handler = new EditorContextMessageHandler(
            envelope =>
            {
                if (envelope.Command.Equals(
                        EditorContextCommands.UpdateSelectionContext,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (_isExiting)
                    {
                        return;
                    }

                    ShowMainWindow();
                    _mainWindow?.OpenChat(newChat: false);
                });
            },
            () => Dispatcher.UIThread.Post(ShowMainWindow),
            composition.Runtime.EditorResponder.RespondAsync);
        _pipeServer = new PersonalAiPipeServer(handler);
        _pipeServer.StateChanged += (_, _) =>
        {
            if (_pipeServer?.State == EditorIpcConnectionState.Unavailable)
            {
                Dispatcher.UIThread.Post(() =>
                    ReportShellStatus("Editor integration is unavailable."));
            }
        };
        _pipeServer.Start();
    }

    /// <summary>
    /// Completes an "Open in AEDA" hand-off.
    /// <para>
    /// Ordering matters. <c>AssistPillViewModel</c> awaits the host callback and only then
    /// sets <c>State = Hidden</c>; hiding the Assist window makes Windows restore focus to
    /// whatever was previously foreground (Notepad in the reported repro). Activating the
    /// main window inline would therefore be undone moments later. Posting at
    /// <see cref="DispatcherPriority.Background"/> defers this until after the Hidden
    /// transition and its focus restoration have run, so AEDA ends up foreground.
    /// </para>
    /// </summary>
    private void SurfaceAssistConversation(Guid conversationId) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_isExiting)
                {
                    return;
                }

                ShowMainWindow();
                _mainWindow?.OpenChat(newChat: false);
            },
            DispatcherPriority.Background);

    private void ShowMainWindow()
    {
        if (!_isExiting)
        {
            _mainWindowActivation?.ShowRestoreAndActivate();
        }
    }

    private void HideMainWindow() => _mainWindowActivation?.Hide();

    private void NewChat()
    {
        if (_isExiting)
        {
            return;
        }

        ShowMainWindow();
        _mainWindow?.OpenChat(newChat: true);
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        if (_composition?.ExitOnMainWindowClose == true)
        {
            RequestExit();
        }
        else if (_composition?.AskBeforeMainWindowExit == true)
        {
            _ = ConfirmExitAsync();
        }
        else
        {
            HideMainWindow();
        }
    }

    private async Task ConfirmExitAsync()
    {
        if (_exitConfirmationOpen || _mainWindow is null)
        {
            return;
        }

        _exitConfirmationOpen = true;
        try
        {
            if (await WorkspaceDialog.ConfirmAsync(
                    _mainWindow,
                    "Exit AEDA",
                    "Exit AEDA instead of keeping it available in the system tray?",
                    "Exit"))
            {
                RequestExit();
            }
            else
            {
                HideMainWindow();
            }
        }
        finally
        {
            _exitConfirmationOpen = false;
        }
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        e.Cancel = true;
        RequestExit();
    }

    private void RequestExit(int exitCode = 0)
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _ = _shutdownCoordinator?.BeginAsync(exitCode);
    }

    private void ReportShellStatus(string message)
    {
        if (_mainWindow is not null)
        {
            _mainWindow.Title = $"AEDA — {message}";
        }
    }

    private async ValueTask DisposeResourcesAsync()
    {
        if (_resourcesDisposed)
        {
            return;
        }

        _resourcesDisposed = true;
        var pipeServer = _pipeServer;
        _pipeServer = null;
        _hotKey?.Dispose();
        _hotKey = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        var composition = _composition;
        _composition = null;
        if (composition is not null)
        {
            composition.SurfaceAssistConversationInShell = null;
        }

        if (_mainWindow is not null)
        {
            _mainWindow.IsEnabled = false;
        }

        if (_assistWindow is not null)
        {
            _assistWindow.IsEnabled = false;
        }

        _assistWindow = null;
        _mainWindow = null;
        _mainWindowActivation = null;
        _textScaleManager?.Dispose();
        _textScaleManager = null;

        var pipeDisposal = pipeServer?.DisposeAsync().AsTask() ?? Task.CompletedTask;
        var compositionDisposal = composition?.DisposeAsync().AsTask() ?? Task.CompletedTask;
        await Task.WhenAll(pipeDisposal, compositionDisposal);
    }
}
