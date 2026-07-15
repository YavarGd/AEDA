using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using PersonalAI.Desktop.WinUI.Services;
using Windows.Graphics;

namespace PersonalAI.Desktop.WinUI.Views;

public sealed partial class ScreenTextCaptureOverlay : Window
{
    private readonly Rectangle _monitorBounds;
    private readonly double _fallbackScale;
    private readonly Action<Rectangle> _selected;
    private readonly Action _cancelled;
    private Windows.Foundation.Point _start;
    private bool _selecting;
    private bool _completed;

    public ScreenTextCaptureOverlay(
        Rectangle monitorBounds,
        double fallbackScale,
        BitmapImage image,
        Action<Rectangle> selected,
        Action cancelled)
    {
        _monitorBounds = monitorBounds;
        _fallbackScale = fallbackScale;
        _selected = selected;
        _cancelled = cancelled;
        InitializeComponent();
        BackgroundImage.Source = image;
        Title = "AEDA screen text selection";
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }
        AppWindow.MoveAndResize(new RectInt32(
            monitorBounds.X,
            monitorBounds.Y,
            monitorBounds.Width,
            monitorBounds.Height));
        Root.Loaded += (_, _) =>
        {
            ResetSelection();
            _ = Root.Focus(FocusState.Programmatic);
        };
        Closed += (_, _) => Cancel();
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _selecting = true;
        _start = point.Position;
        _ = Root.CapturePointer(e.Pointer);
        UpdateSelection(point.Position);
        e.Handled = true;
    }

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        _ = SetCursor(LoadCursor(0, 32515));

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_selecting)
        {
            UpdateSelection(e.GetCurrentPoint(Root).Position);
        }
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_selecting)
        {
            return;
        }

        _selecting = false;
        Root.ReleasePointerCapture(e.Pointer);
        var end = e.GetCurrentPoint(Root).Position;
        UpdateSelection(end);
        var scale = Root.XamlRoot?.RasterizationScale ?? _fallbackScale;
        var region = ScreenRegionGeometry.FromDrag(
            _monitorBounds,
            scale,
            new PointF((float)_start.X, (float)_start.Y),
            new PointF((float)end.X, (float)end.Y));
        if (region is null)
        {
            ResetSelection();
            return;
        }

        _completed = true;
        _selected(region.Value);
        e.Handled = true;
    }

    private void Root_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _selecting = false;
        Root.ReleasePointerCapture(e.Pointer);
        ResetSelection();
    }

    private void Root_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        Cancel();
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            Cancel();
        }
    }

    private void UpdateSelection(Windows.Foundation.Point current)
    {
        var width = Math.Max(0, Root.ActualWidth);
        var height = Math.Max(0, Root.ActualHeight);
        var x = Math.Clamp(current.X, 0, width);
        var y = Math.Clamp(current.Y, 0, height);
        var left = Math.Min(_start.X, x);
        var top = Math.Min(_start.Y, y);
        var right = Math.Max(_start.X, x);
        var bottom = Math.Max(_start.Y, y);

        SetRect(TopShade, 0, 0, width, top);
        SetRect(BottomShade, 0, bottom, width, height - bottom);
        SetRect(LeftShade, 0, top, left, bottom - top);
        SetRect(RightShade, right, top, width - right, bottom - top);
        SetRect(SelectionBorder, left, top, right - left, bottom - top);
        SelectionBorder.Visibility = Visibility.Visible;
    }

    private void ResetSelection()
    {
        SetRect(TopShade, 0, 0, Root.ActualWidth, Root.ActualHeight);
        SetRect(BottomShade, 0, 0, 0, 0);
        SetRect(LeftShade, 0, 0, 0, 0);
        SetRect(RightShade, 0, 0, 0, 0);
        SelectionBorder.Visibility = Visibility.Collapsed;
    }

    private static void SetRect(
        FrameworkElement element,
        double left,
        double top,
        double width,
        double height)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }

    private void Cancel()
    {
        if (!_completed)
        {
            _completed = true;
            _cancelled();
        }
    }

    [DllImport("user32.dll")]
    private static extern nint LoadCursor(nint instance, int cursorName);

    [DllImport("user32.dll")]
    private static extern nint SetCursor(nint cursor);
}
