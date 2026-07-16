using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PersonalAI.Core.Context;
using PersonalAI.Core.Ui;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Desktop.WinUI.ViewModels;
using PersonalAI.Infrastructure.Context;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace PersonalAI.Desktop.WinUI.Views;

public sealed partial class AssistPillWindow : Window
{
    public const int IdleWidth = 52;
    public const int IdleHeight = 52;
    public const int ResponseResizeIntervalMilliseconds = 150;
    private const int SpotlightWidth = 520;
    private const int SpotlightHeight = 64;
    private const int ResponseChromeHeight = 132;

    private readonly AssistPillViewModel _viewModel;
    private readonly WinUiWindowPlacementService _placementService;
    private readonly ForegroundWindowTracker _foregroundWindowTracker;
    private readonly nint _windowHandle;
    private readonly DispatcherQueueTimer _responseResizeTimer;
    private readonly DispatcherQueueTimer _heightAnimationTimer;
    private readonly AssistHeightInterpolator _heightInterpolator = new();
    private readonly Stopwatch _motionClock = Stopwatch.StartNew();
    private readonly SystemBackdrop? _expandedBackdrop;
    private readonly UISettings _uiSettings = new();
    private CancellationTokenSource? _openingCancellation;
    private Storyboard? _launcherReleaseStoryboard;
    private Storyboard? _surfaceEntranceStoryboard;
    private Storyboard? _firstContentStoryboard;
    private nint _focusReturnWindow;
    private int _isTransitioning;
    private bool _followLatest = true;
    private long _pendingResizeInvocationId;
    private long _responseEntranceInvocationId;
    private long _spotlightEntranceInvocationId;
    private long _firstContentInvocationId;
    private AssistInvocationKind _invocationKind = AssistInvocationKind.Keyboard;

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
        _responseResizeTimer = Root.DispatcherQueue.CreateTimer();
        _responseResizeTimer.Interval = TimeSpan.FromMilliseconds(
            ResponseResizeIntervalMilliseconds);
        _responseResizeTimer.Tick += (_, _) =>
        {
            _responseResizeTimer.Stop();
            if (_pendingResizeInvocationId != _viewModel.InvocationId)
            {
                return;
            }

            ApplyResponseSize();
            FollowLatestIfNeeded();
        };
        _heightAnimationTimer = Root.DispatcherQueue.CreateTimer();
        _heightAnimationTimer.Interval = TimeSpan.FromMilliseconds(16);
        _heightAnimationTimer.Tick += (_, _) => ApplyHeightAnimationFrame();
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        if (MicaController.IsSupported())
        {
            _expandedBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            _expandedBackdrop = new DesktopAcrylicBackdrop();
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
        AedaWindowChrome.Apply(AppWindow, _windowHandle, Root.ActualTheme == ElementTheme.Dark);
        _placementService.ConfigureWindow(this, rememberPositionChanges: false);
        SetNoActivate(true);
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += (_, _) => CancelMotion();
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

    public async Task ToggleAsync(bool pointerTriggered = false)
    {
        if (Interlocked.CompareExchange(ref _isTransitioning, 1, 0) != 0)
        {
            _openingCancellation?.Cancel();
            return;
        }

        try
        {
            _invocationKind = pointerTriggered
                ? AssistInvocationKind.Pointer
                : AssistInvocationKind.Keyboard;
            if (_viewModel.IsExpanded)
            {
                if (_viewModel.IsDetectingContext)
                {
                    _openingCancellation?.Cancel();
                    _viewModel.CancelContextCapture();
                }
                else if (_viewModel.IsStreaming)
                {
                    _viewModel.Cancel();
                }
                else
                {
                    _viewModel.Collapse();
                    RestoreFocus();
                }

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
        AedaWindowChrome.Apply(AppWindow, _windowHandle, theme == ElementTheme.Dark);
    }

    private async Task OpenPromptAsync()
    {
        CaptureFocusReturnWindow();
        using var cancellation = new CancellationTokenSource();
        _openingCancellation = cancellation;
        try
        {
            var opening = _viewModel.OpenPromptAsync(cancellation.Token);
            var opened = await opening;
            if (!opened || !_viewModel.IsEnabled)
            {
                return;
            }

            ActivateSurface();
        }
        finally
        {
            if (ReferenceEquals(_openingCancellation, cancellation))
            {
                _openingCancellation = null;
            }
        }
    }

    private void ActivateSurface()
    {
        AppWindow.Show(activateWindow: true);
        _ = SetForegroundWindow(_windowHandle);
        if (_viewModel.IsFallbackInput)
        {
            _ = Root.DispatcherQueue.TryEnqueue(() =>
            {
                PromptTextBox.Focus(FocusState.Programmatic);
                PromptTextBox.SelectionStart = PromptTextBox.Text.Length;
            });
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssistPillViewModel.State))
        {
            if (_viewModel.State is AssistPillState.DetectingContext or
                AssistPillState.StreamingResponse)
            {
                _followLatest = true;
            }

            if (!_viewModel.IsStreaming)
            {
                CancelHeightAnimation();
            }

            ApplyState(
                reposition: _viewModel.State is AssistPillState.IdlePill or
                    AssistPillState.SpotlightPrompt ||
                    _viewModel.IsResponseSurface);
        }
        else if (e.PropertyName == nameof(AssistPillViewModel.InvocationId))
        {
            CancelMotion();
            _pendingResizeInvocationId = _viewModel.InvocationId;
            _followLatest = true;
            SetResponseScrolling(false);
            _ = ResponseScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
        }
        else if (e.PropertyName == nameof(AssistPillViewModel.Response) &&
            _viewModel.IsResponseSurface &&
            !_responseResizeTimer.IsRunning)
        {
            _pendingResizeInvocationId = _viewModel.InvocationId;
            _responseResizeTimer.Start();
            StartFirstContentFade();
        }
    }

    private void ApplyState(bool reposition)
    {
        if (_viewModel.State == AssistPillState.Hidden)
        {
            CancelMotion();
            AppWindow.Hide();
            return;
        }

        if (!_viewModel.IsResponseSurface)
        {
            CancelHeightAnimation();
        }

        var scale = GetRasterizationScale();
        var size = _viewModel.IsIdle
            ? new SizeInt32(
                AssistResponseSizingPolicy.ScalePixels(IdleWidth, scale),
                AssistResponseSizingPolicy.ScalePixels(IdleHeight, scale))
            : _viewModel.IsFallbackInput
                ? new SizeInt32(
                    AssistResponseSizingPolicy.ScalePixels(SpotlightWidth, scale),
                    AssistResponseSizingPolicy.ScalePixels(SpotlightHeight, scale))
                : GetResponseSize();
        AppWindow.Resize(size);
        ApplyWindowShape();
        SetNoActivate(_viewModel.IsIdle);
        StartSurfaceEntrance();

        if (!reposition)
        {
            return;
        }

        var externalWindow = _foregroundWindowTracker.GetLastValidExternalWindow();
        if (_viewModel.IsIdle)
        {
            _placementService.PlaceAssistPill(this, externalWindow);
        }
        else
        {
            _placementService.PlaceCentered(this, externalWindow);
        }
    }

    private SizeInt32 GetResponseSize()
    {
        var externalWindow = _foregroundWindowTracker.GetLastValidExternalWindow();
        var area = _placementService.GetActivationWorkingArea(externalWindow);
        var scale = GetRasterizationScale();
        var availableWidth = Math.Max(1, (area.Width / scale) - 40);
        ResponsePresenter.Measure(new Windows.Foundation.Size(
            Math.Max(1, Math.Min(560, availableWidth) - 56),
            double.PositiveInfinity));
        var layout = AssistResponseSizingPolicy.CalculateMeasured(
            ResponsePresenter.DesiredSize.Height + ResponseChromeHeight,
            area,
            scale);
        SetResponseScrolling(layout.RequiresScrolling);
        return new SizeInt32(layout.Width, layout.Height);
    }

    private double GetRasterizationScale() =>
        Root.XamlRoot?.RasterizationScale ?? Math.Max(1, GetDpiForWindow(_windowHandle) / 96d);

    private void ApplyResponseSize()
    {
        if (!_viewModel.IsResponseSurface)
        {
            return;
        }

        var target = GetResponseSize();
        var scale = GetRasterizationScale();
        var area = _placementService.GetActivationWorkingArea(
            _foregroundWindowTracker.GetLastValidExternalWindow());
        var maximum = Math.Max(1, Math.Min(
            AssistResponseSizingPolicy.ScalePixels(480, scale),
            (int)Math.Round(area.Height - (40 * scale))));
        var current = AppWindow.Size;
        var height = _heightInterpolator.Retarget(
            current.Height,
            target.Height,
            maximum,
            _viewModel.IsStreaming,
            _uiSettings.AnimationsEnabled,
            _viewModel.InvocationId,
            _motionClock.Elapsed);
        ResizeResponseWindow(target.Width, height);
        if (_heightInterpolator.IsActive)
        {
            _heightAnimationTimer.Start();
        }
    }

    private void ApplyHeightAnimationFrame()
    {
        var invocationId = _viewModel.InvocationId;
        if (!_viewModel.IsResponseSurface ||
            !_heightInterpolator.TrySample(_motionClock.Elapsed, invocationId, out var height))
        {
            _heightAnimationTimer.Stop();
            return;
        }

        ResizeResponseWindow(AppWindow.Size.Width, height);
        if (!_heightInterpolator.IsActive)
        {
            _heightAnimationTimer.Stop();
            FollowLatestIfNeeded();
        }
    }

    private void ResizeResponseWindow(int width, int height)
    {
        AppWindow.Resize(new SizeInt32(width, height));
        ApplyWindowShape();
        _placementService.PlaceCentered(
            this,
            _foregroundWindowTracker.GetLastValidExternalWindow());
    }

    private void StartSurfaceEntrance()
    {
        if (_viewModel.IsIdle)
        {
            return;
        }

        var spotlight = _viewModel.IsFallbackInput;
        ref var animatedInvocationId = ref (spotlight
            ? ref _spotlightEntranceInvocationId
            : ref _responseEntranceInvocationId);
        if (animatedInvocationId == _viewModel.InvocationId)
        {
            return;
        }

        animatedInvocationId = _viewModel.InvocationId;
        var motion = AssistMotionPolicy.Entrance(
            _invocationKind,
            spotlight,
            _uiSettings.AnimationsEnabled);
        var surface = spotlight ? SpotlightSurface : ResponseCard;
        var transform = spotlight ? SpotlightScale : ResponseScale;
        _surfaceEntranceStoryboard?.Stop();
        surface.Opacity = 1;
        transform.ScaleX = 1;
        transform.ScaleY = 1;
        if (!motion.IsSpatial)
        {
            return;
        }

        surface.Opacity = 0;
        transform.ScaleX = motion.InitialScale;
        transform.ScaleY = motion.InitialScale;
        var duration = new Duration(TimeSpan.FromMilliseconds(motion.DurationMilliseconds));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        _surfaceEntranceStoryboard = new Storyboard();
        AddAnimation(_surfaceEntranceStoryboard, surface, nameof(UIElement.Opacity), 1, duration, easing);
        AddAnimation(_surfaceEntranceStoryboard, transform, nameof(ScaleTransform.ScaleX), 1, duration, easing);
        AddAnimation(_surfaceEntranceStoryboard, transform, nameof(ScaleTransform.ScaleY), 1, duration, easing);
        _surfaceEntranceStoryboard.Begin();
    }

    private void StartFirstContentFade()
    {
        if (string.IsNullOrEmpty(_viewModel.Response) ||
            _firstContentInvocationId == _viewModel.InvocationId)
        {
            return;
        }

        _firstContentInvocationId = _viewModel.InvocationId;
        _firstContentStoryboard?.Stop();
        ResponsePresenter.Opacity = 1;
        if (!_uiSettings.AnimationsEnabled)
        {
            return;
        }

        ResponsePresenter.Opacity = 0;
        _firstContentStoryboard = new Storyboard();
        AddAnimation(
            _firstContentStoryboard,
            ResponsePresenter,
            nameof(UIElement.Opacity),
            1,
            new Duration(TimeSpan.FromMilliseconds(AssistMotionPolicy.FirstContentFadeMilliseconds)),
            new CubicEase { EasingMode = EasingMode.EaseOut });
        _firstContentStoryboard.Begin();
    }

    private static void AddAnimation(
        Storyboard storyboard,
        DependencyObject target,
        string property,
        double to,
        Duration duration,
        EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private void CancelHeightAnimation()
    {
        _heightAnimationTimer.Stop();
        _heightInterpolator.Cancel();
    }

    private void CancelMotion()
    {
        _responseResizeTimer.Stop();
        CancelHeightAnimation();
        _surfaceEntranceStoryboard?.Stop();
        _firstContentStoryboard?.Stop();
        SpotlightSurface.Opacity = 1;
        ResponseCard.Opacity = 1;
        ResponsePresenter.Opacity = 1;
        SpotlightScale.ScaleX = SpotlightScale.ScaleY = 1;
        ResponseScale.ScaleX = ResponseScale.ScaleY = 1;
    }

    private void ResponseScrollViewer_ViewChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs e)
    {
        _followLatest = AssistScrollFollowPolicy.IsNearBottom(
            ResponseScrollViewer.ScrollableHeight,
            ResponseScrollViewer.VerticalOffset);
    }

    private void FollowLatestIfNeeded()
    {
        if (!_followLatest || !_viewModel.IsResponseSurface || _viewModel.IsDetectingContext)
        {
            return;
        }

        var invocationId = _viewModel.InvocationId;
        _ = Root.DispatcherQueue.TryEnqueue(() =>
        {
            if (invocationId == _viewModel.InvocationId && _followLatest)
            {
                ResponseScrollViewer.ChangeView(
                    null,
                    ResponseScrollViewer.ScrollableHeight,
                    null,
                    disableAnimation: true);
            }
        });
    }

    private void SetResponseScrolling(bool enabled)
    {
        ResponseScrollViewer.VerticalScrollMode = enabled
            ? ScrollMode.Auto
            : ScrollMode.Disabled;
        ResponseScrollViewer.VerticalScrollBarVisibility = enabled
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Hidden;
    }

    private async void OpenPromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsDetectingContext || Volatile.Read(ref _isTransitioning) != 0)
        {
            System.Diagnostics.Debug.WriteLine(
                "AEDA Assist activation ignored: duplicate-activation-ignored.");
            return;
        }

        await ToggleAsync(pointerTriggered: true);
    }

    private async void SelectScreenTextButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsFallbackInput)
        {
            return;
        }

        AppWindow.Hide();
        _ = DwmFlush();
        await _viewModel.SelectScreenTextAsync();
        if (_viewModel.State != AssistPillState.Hidden)
        {
            ActivateSurface();
        }
    }

    private void LauncherButton_PointerPressed(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (!_viewModel.IsDetectingContext && Volatile.Read(ref _isTransitioning) == 0)
        {
            _ = _foregroundWindowTracker.CaptureCurrentExternalWindow(_windowHandle);
        }

        _launcherReleaseStoryboard?.Stop();
        if (!_uiSettings.AnimationsEnabled)
        {
            return;
        }

        LauncherScale.ScaleX = 0.97;
        LauncherScale.ScaleY = 0.97;
    }

    private void LauncherButton_PointerReleased(
        object sender,
        PointerRoutedEventArgs e)
    {
        _launcherReleaseStoryboard?.Stop();
        if (!_uiSettings.AnimationsEnabled)
        {
            LauncherScale.ScaleX = 1;
            LauncherScale.ScaleY = 1;
            return;
        }

        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(120));
        var scaleX = new DoubleAnimation { To = 1, Duration = duration, EasingFunction = easing };
        var scaleY = new DoubleAnimation { To = 1, Duration = duration, EasingFunction = easing };
        Storyboard.SetTarget(scaleX, LauncherScale);
        Storyboard.SetTarget(scaleY, LauncherScale);
        Storyboard.SetTargetProperty(scaleX, nameof(ScaleTransform.ScaleX));
        Storyboard.SetTargetProperty(scaleY, nameof(ScaleTransform.ScaleY));
        _launcherReleaseStoryboard = new Storyboard();
        _launcherReleaseStoryboard.Children.Add(scaleX);
        _launcherReleaseStoryboard.Children.Add(scaleY);
        _launcherReleaseStoryboard.Begin();
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
        else if (_viewModel.IsDetectingContext)
        {
            _openingCancellation?.Cancel();
            _viewModel.CancelContextCapture();
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

    private void ApplyWindowShape()
    {
        SystemBackdrop = _viewModel.IsIdle ? null : _expandedBackdrop;
        var size = AppWindow.Size;
        var radius = AssistResponseSizingPolicy.ScalePixels(
            _viewModel.IsIdle ? IdleHeight / 2 : 20,
            GetDpiForWindow(_windowHandle) / 96d);
        var region = CreateRoundRectRgn(
            0,
            0,
            size.Width + 1,
            size.Height + 1,
            radius * 2,
            radius * 2);
        if (region != 0 && SetWindowRgn(_windowHandle, region, true) == 0)
        {
            _ = DeleteObject(region);
        }
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

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint hWnd, nint hRgn, bool redraw);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int widthEllipse,
        int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

}
