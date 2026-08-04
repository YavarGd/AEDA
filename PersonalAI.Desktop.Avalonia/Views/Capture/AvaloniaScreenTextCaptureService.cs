using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PersonalAI.Desktop.Presentation.Services;
using PersonalAI.Infrastructure.ScreenCapture;
using PersonalAI.Infrastructure.Windows;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace PersonalAI.Desktop.Avalonia.Views.Capture;

public sealed class AvaloniaScreenTextCaptureService : IScreenTextCaptureService
{
    private readonly Func<IReadOnlyList<Screen>> _getScreens;
    private readonly ScreenTextCaptureEngine _engine;

    public AvaloniaScreenTextCaptureService(Func<IReadOnlyList<Screen>> getScreens)
    {
        _getScreens = getScreens ?? throw new ArgumentNullException(nameof(getScreens));
        _engine = new ScreenTextCaptureEngine(
            new WindowsOcrTextRecognizer(),
            CaptureRegionAsync);
    }

    public Task<ScreenTextCaptureResult> CaptureAsync(
        int maxCharacters,
        int maxPayloadBytes,
        CancellationToken cancellationToken) =>
        _engine.CaptureAsync(maxCharacters, maxPayloadBytes, cancellationToken);

    private async Task<ScreenRegionCapture?> CaptureRegionAsync(
        CancellationToken cancellationToken)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return await CaptureRegionOnUiThreadAsync(cancellationToken);
        }

        return await Dispatcher.UIThread.InvokeAsync(
            () => CaptureRegionOnUiThreadAsync(cancellationToken));
    }

    private async Task<ScreenRegionCapture?> CaptureRegionOnUiThreadAsync(
        CancellationToken cancellationToken)
    {
        var screens = _getScreens();
        if (screens.Count == 0)
        {
            return null;
        }

        var captures = new List<MonitorCapture>();
        var overlays = new List<ScreenTextCaptureOverlay>();
        var completion = new TaskCompletionSource<SelectedRegion?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completing = 0;

        void Complete(SelectedRegion? selection)
        {
            if (Interlocked.Exchange(ref completing, 1) == 0)
            {
                completion.TrySetResult(selection);
            }
        }

        try
        {
            foreach (var screen in screens)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bounds = new DrawingRectangle(
                    screen.Bounds.X,
                    screen.Bounds.Y,
                    screen.Bounds.Width,
                    screen.Bounds.Height);
                var bitmap = new DrawingBitmap(
                    bounds.Width,
                    bounds.Height,
                    DrawingPixelFormat.Format32bppArgb);
                using (var graphics = DrawingGraphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        bounds.Location,
                        DrawingPoint.Empty,
                        bounds.Size,
                        System.Drawing.CopyPixelOperation.SourceCopy);
                }

                var source = CreateImageSource(bitmap);
                captures.Add(new MonitorCapture(bounds, bitmap));
                overlays.Add(new ScreenTextCaptureOverlay(
                    bounds,
                    screen.Scaling,
                    source,
                    region => Complete(new SelectedRegion(bounds, region)),
                    () => Complete(null)));
            }

            using var registration = cancellationToken.Register(() =>
                Dispatcher.UIThread.Post(() => Complete(null)));
            foreach (var overlay in overlays)
            {
                overlay.Show();
            }
            overlays[^1].Activate();

            var selected = await completion.Task;
            foreach (var overlay in overlays)
            {
                overlay.Close();
            }

            if (selected is null)
            {
                return null;
            }

            var capture = captures.Single(item => item.Bounds == selected.MonitorBounds);
            captures.Remove(capture);
            var local = selected.Region;
            local.Offset(-capture.Bounds.X, -capture.Bounds.Y);
            return new ScreenRegionCapture(capture.Bitmap, local);
        }
        finally
        {
            foreach (var overlay in overlays)
            {
                try { overlay.Close(); } catch { }
            }
            foreach (var capture in captures)
            {
                capture.Bitmap.Dispose();
            }
        }
    }

    private static Bitmap CreateImageSource(DrawingBitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, DrawingImageFormat.Png);
        stream.Position = 0;
        return new Bitmap(stream);
    }

    private sealed record MonitorCapture(DrawingRectangle Bounds, DrawingBitmap Bitmap);
    private sealed record SelectedRegion(
        DrawingRectangle MonitorBounds,
        DrawingRectangle Region);
}
