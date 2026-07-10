using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PersonalAI.Core.Context;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Desktop.WinUI.ViewModels;
using PersonalAI.Infrastructure.Context;
using Windows.Graphics;

namespace PersonalAI.Desktop.WinUI.Views;

public sealed partial class AssistPillWindow : Window
{
    public const int IdleWidth = 56;
    public const int IdleHeight = 56;
    private const int ContextWidth = 500;
    private const int ContextHeight = 380;
    private const int SpotlightWidth = 640;
    private const int SpotlightHeight = 340;

    private readonly AssistPillViewModel _viewModel;
    private readonly WinUiWindowPlacementService _placementService;
    private readonly ForegroundWindowTracker _foregroundWindowTracker;
    private readonly nint _windowHandle;
    private nint _focusReturnWindow;
    private int _isTransitioning;

    public AssistPillWindow(
        AssistPillViewModel viewModel,
        WinUiWindowPlacementService placementService,
        ForegroundWindowTracker foregroundWindowTracker)
    {
        _viewModel = viewModel;
        _placementService = placementService;
        _foregroundWindowTracker = foregroundWindowTracker;
        InitializeComponent();
        Root.DataContext = _viewModel;
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        AppWindow.IsShownInSwitchers = false;
        ConfigurePopupStyle();
        SetRoundedCorners();
        _placementService.ConfigureWindow(this, rememberPositionChanges: false);
        SetNoActivate(true);
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    public void ShowIdle()
    {
        _viewModel.ShowIdle();
        ApplyState(reposition: true);
        if (_viewModel.IsEnabled)
        {
            AppWindow.Show(activateWindow: false);
        }
        else
        {
            AppWindow.Hide();
        }
    }

    public async Task ToggleAsync()
    {
        if (Interlocked.CompareExchange(ref _isTransitioning, 1, 0) != 0)
        {
            if (_viewModel.IsExpanded)
            {
                ActivatePrompt();
            }

            return;
        }

        try
        {
            if (_viewModel.IsExpanded)
            {
                ActivatePrompt();
                return;
            }

            await OpenPromptAsync();
        }
        catch
        {
            _viewModel.ShowIdle();
            ApplyState(reposition: true);
            AppWindow.Show(activateWindow: false);
        }
        finally
        {
            Interlocked.Exchange(ref _isTransitioning, 0);
        }
    }

    public void ApplyTheme(ElementTheme theme)
    {
        Root.RequestedTheme = theme;
    }

    private async Task OpenPromptAsync()
    {
        CaptureFocusReturnWindow();
        var opened = await _viewModel.OpenPromptAsync();
        if (!opened || !_viewModel.IsEnabled)
        {
            return;
        }

        ActivatePrompt();
    }

    private void ActivatePrompt()
    {
        AppWindow.Show(activateWindow: true);
        PromptTextBox.Focus(FocusState.Programmatic);
        PromptTextBox.SelectionStart = PromptTextBox.Text.Length;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssistPillViewModel.State))
        {
            ApplyState(
                reposition: _viewModel.State is AssistPillState.IdlePill or
                    AssistPillState.ContextPrompt or AssistPillState.SpotlightPrompt);
        }
    }

    private void ApplyState(bool reposition)
    {
        if (_viewModel.State == AssistPillState.Hidden)
        {
            AppWindow.Hide();
            return;
        }

        var size = _viewModel.IsIdle
            ? new SizeInt32(IdleWidth, IdleHeight)
            : _viewModel.HasContext
                ? new SizeInt32(ContextWidth, ContextHeight)
                : new SizeInt32(SpotlightWidth, SpotlightHeight);
        AppWindow.Resize(size);
        SetNoActivate(_viewModel.IsIdle);

        if (!reposition)
        {
            return;
        }

        var externalWindow = _foregroundWindowTracker.GetLastValidExternalWindow();
        if (_viewModel.IsIdle)
        {
            _placementService.PlaceAssistPill(this, externalWindow);
        }
        else if (!_viewModel.HasContext)
        {
            _placementService.PlaceCentered(this, externalWindow);
        }
        else
        {
            _placementService.PlaceExpandedNearRemembered(this, externalWindow);
        }
    }

    private async void OpenPromptButton_Click(object sender, RoutedEventArgs e)
    {
        await ToggleAsync();
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Collapse();
        RestoreFocus();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Hide();
        RestoreFocus();
    }

    private void PromptTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        var shiftDown = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ComposerKeyboardInteraction.ForEnter(
                shiftDown,
                _viewModel.SubmitCommand.CanExecute(null),
                PromptTextBox.Text) == ComposerKeyboardAction.Send)
        {
            e.Handled = true;
            _viewModel.SubmitCommand.Execute(null);
        }
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        if (_viewModel.IsStreaming)
        {
            _viewModel.Cancel();
        }
        else if (_viewModel.IsExpanded)
        {
            _viewModel.Collapse();
            RestoreFocus();
        }
        else
        {
            _viewModel.Hide();
            RestoreFocus();
        }
    }

    private void DragRegion_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        _ = ReleaseCapture();
        _ = SendMessage(_windowHandle, 0x00A1, 2, 0);
    }

    private void IdleDragRegion_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        _ = ReleaseCapture();
        _ = SendMessage(_windowHandle, 0x00A1, 2, 0);
        _placementService.RememberPosition(this);
    }

    private void CaptureFocusReturnWindow()
    {
        var foreground = GetForegroundWindow();
        if (foreground != 0 && foreground != _windowHandle && IsWindow(foreground))
        {
            _focusReturnWindow = foreground;
            return;
        }

        if (_foregroundWindowTracker.IsLastObservedExternalWindowSafe)
        {
            _focusReturnWindow =
                _foregroundWindowTracker.GetLastValidExternalWindow()?.WindowHandle ?? 0;
        }
    }

    private void RestoreFocus()
    {
        if (_focusReturnWindow != 0 && IsWindow(_focusReturnWindow))
        {
            _ = SetForegroundWindow(_focusReturnWindow);
        }
    }

    private void SetRoundedCorners()
    {
        const int windowCornerPreference = 33;
        var preference = 3;
        _ = DwmSetWindowAttribute(
            _windowHandle,
            windowCornerPreference,
            ref preference,
            sizeof(int));
    }

    private void ConfigurePopupStyle()
    {
        const int styleIndex = -16;
        const long popup = 0x80000000;
        const long caption = 0x00C00000;
        const long thickFrame = 0x00040000;
        const long minimizeBox = 0x00020000;
        const long maximizeBox = 0x00010000;
        const long systemMenu = 0x00080000;
        const uint noMove = 0x0002;
        const uint noSize = 0x0001;
        const uint noActivate = 0x0010;
        const uint frameChanged = 0x0020;

        var style = GetWindowLongPtr(_windowHandle, styleIndex).ToInt64();
        style &= ~(caption | thickFrame | minimizeBox | maximizeBox | systemMenu);
        style |= popup;
        _ = SetWindowLongPtr(_windowHandle, styleIndex, new nint(style));
        _ = SetWindowPos(
            _windowHandle,
            0,
            0,
            0,
            0,
            0,
            noMove | noSize | noActivate | frameChanged);
    }

    private void SetNoActivate(bool enabled)
    {
        const int extendedStyleIndex = -20;
        const long noActivate = 0x08000000;
        var style = GetWindowLongPtr(_windowHandle, extendedStyleIndex).ToInt64();
        style = enabled ? style | noActivate : style & ~noActivate;
        _ = SetWindowLongPtr(_windowHandle, extendedStyleIndex, new nint(style));
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int index, nint newValue);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int value,
        int valueSize);

}
