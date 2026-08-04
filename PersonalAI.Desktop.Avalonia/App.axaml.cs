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
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;
using PersonalAI.Desktop.Avalonia.Views.Dialogs;
using PersonalAI.Infrastructure.Ipc;
using PersonalAI.Infrastructure.Windows;

namespace PersonalAI.Desktop.Avalonia;

public partial class App : Application
{
    private WindowsSingleInstanceService? _singleInstanceService;
    private AvaloniaAppComposition? _composition;
    private MainWindow? _mainWindow;
    private AvaloniaAssistWindow? _assistWindow;
    private AvaloniaWindowActivationService? _mainWindowActivation;
    private AvaloniaTrayIconService? _trayIcon;
    private WindowsGlobalHotKeyService? _hotKey;
    private PersonalAiPipeServer? _pipeServer;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private bool _isExiting;
    private bool _resourcesDisposed;
    private bool _exitConfirmationOpen;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _desktop = desktop;
        desktop.Exit += (_, _) => DisposeResources();
        try
        {
            _singleInstanceService = new WindowsSingleInstanceService();
            if (!_singleInstanceService.IsPrimaryInstance)
            {
                _singleInstanceService.Dispose();
                _singleInstanceService = null;
                desktop.Shutdown();
                return;
            }

            var window = new MainWindow();
            _ = InitializeCompositionAsync(desktop, window);
        }
        catch
        {
            DisposeResources();
            desktop.Shutdown(1);
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
            _composition = await AvaloniaAppComposition.CreateAsync(
                action => Dispatcher.UIThread.Post(action));
            await _composition.Chat.InitializeAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
                CompleteInitialization(desktop, window));
        }
        catch
        {
            DisposeResources();
            desktop.Shutdown(1);
        }
    }

    private void CompleteInitialization(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow window)
    {
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
        _composition.Chat.PropertyChanged += OnChatPropertyChanged;

        _trayIcon = new AvaloniaTrayIconService(
            ShowMainWindow,
            NewChat,
            RequestExit);
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

    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AvaloniaChatViewModel.ActiveConversation) &&
            _mainWindow?.IsVisible == false)
        {
            ShowMainWindow();
            _mainWindow.OpenChat(newChat: false);
        }
    }

    private void ShowMainWindow() => _mainWindowActivation?.ShowRestoreAndActivate();

    private void HideMainWindow() => _mainWindowActivation?.Hide();

    private void NewChat()
    {
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

    private void RequestExit()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        Dispatcher.UIThread.Post(() => _desktop?.Shutdown());
    }

    private void ReportShellStatus(string message)
    {
        if (_mainWindow is not null)
        {
            _mainWindow.Title = $"AEDA — {message}";
        }
    }

    private void DisposeResources()
    {
        if (_resourcesDisposed)
        {
            return;
        }

        _resourcesDisposed = true;
        _pipeServer?.Dispose();
        _pipeServer = null;
        _hotKey?.Dispose();
        _hotKey = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        if (_composition is not null)
        {
            _composition.Chat.PropertyChanged -= OnChatPropertyChanged;
            _composition.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _composition = null;
        }

        _assistWindow = null;
        _mainWindow = null;
        _mainWindowActivation = null;
        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
    }
}
