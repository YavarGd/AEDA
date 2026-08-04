using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using PersonalAI.Infrastructure.ScreenCapture;
using DrawingPointF = System.Drawing.PointF;
using DrawingRectangle = System.Drawing.Rectangle;

namespace PersonalAI.Desktop.Avalonia.Views.Capture;

public partial class ScreenTextCaptureOverlay : Window
{
    private readonly DrawingRectangle _monitorBounds = DrawingRectangle.Empty;
    private readonly double _scale = 1;
    private readonly Action<DrawingRectangle> _selected = _ => { };
    private readonly Action _cancelled = () => { };
    private Point _start;
    private bool _selecting;
    private bool _completed;

    public ScreenTextCaptureOverlay()
    {
        InitializeComponent();
    }

    public ScreenTextCaptureOverlay(
        DrawingRectangle monitorBounds,
        double scale,
        Bitmap image,
        Action<DrawingRectangle> selected,
        Action cancelled) : this()
    {
        _monitorBounds = monitorBounds;
        _scale = scale;
        _selected = selected;
        _cancelled = cancelled;
        BackgroundImage.Source = image;
        Position = new PixelPoint(monitorBounds.X, monitorBounds.Y);
        Width = monitorBounds.Width / scale;
        Height = monitorBounds.Height / scale;
        Opened += (_, _) =>
        {
            ResetSelection();
            Root.Focus();
        };
        Closed += (_, _) =>
        {
            image.Dispose();
            Cancel();
        };
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root);
        if (point.Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            Cancel();
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _selecting = true;
        _start = point.Position;
        e.Pointer.Capture(Root);
        UpdateSelection(point.Position);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_selecting)
        {
            UpdateSelection(e.GetPosition(Root));
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_selecting)
        {
            return;
        }

        _selecting = false;
        e.Pointer.Capture(null);
        var end = e.GetPosition(Root);
        UpdateSelection(end);
        var region = ScreenRegionGeometry.FromDrag(
            _monitorBounds,
            _scale,
            new DrawingPointF((float)_start.X, (float)_start.Y),
            new DrawingPointF((float)end.X, (float)end.Y));
        if (region is null)
        {
            ResetSelection();
            return;
        }

        _completed = true;
        _selected(region.Value);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_completed)
        {
            _selecting = false;
            ResetSelection();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
        }
    }

    private void UpdateSelection(Point current)
    {
        var width = Math.Max(0, Root.Bounds.Width);
        var height = Math.Max(0, Root.Bounds.Height);
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
        SelectionBorder.IsVisible = true;
    }

    private void ResetSelection()
    {
        SetRect(TopShade, 0, 0, Root.Bounds.Width, Root.Bounds.Height);
        SetRect(BottomShade, 0, 0, 0, 0);
        SetRect(LeftShade, 0, 0, 0, 0);
        SetRect(RightShade, 0, 0, 0, 0);
        SelectionBorder.IsVisible = false;
    }

    private static void SetRect(
        Control control,
        double left,
        double top,
        double width,
        double height)
    {
        Canvas.SetLeft(control, left);
        Canvas.SetTop(control, top);
        control.Width = Math.Max(0, width);
        control.Height = Math.Max(0, height);
    }

    private void Cancel()
    {
        if (!_completed)
        {
            _completed = true;
            _cancelled();
        }
    }
}
