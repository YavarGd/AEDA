using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media.Imaging;
using PersonalAI.Desktop.Presentation.Services;
using PersonalAI.Desktop.WinUI.Views;
using PersonalAI.Infrastructure.ScreenCapture;
using PersonalAI.Infrastructure.Windows;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class ScreenTextCaptureService : IScreenTextCaptureService
{
    private readonly ScreenTextCaptureEngine _engine;

    public ScreenTextCaptureService(
        IWindowsOcrTextRecognizer? recognizer = null,
        Func<CancellationToken, Task<ScreenRegionCapture?>>? captureRegion = null,
        TimeSpan? recognitionTimeout = null)
    {
        _engine = new ScreenTextCaptureEngine(
            recognizer ?? new WindowsOcrTextRecognizer(),
            captureRegion ?? CaptureRegionAsync,
            recognitionTimeout);
    }

    public Task<ScreenTextCaptureResult> CaptureAsync(
        int maxCharacters,
        CancellationToken cancellationToken) =>
        CaptureAsync(
            maxCharacters,
            ScreenTextCaptureEngine.DefaultMaxPayloadBytes,
            cancellationToken);

    public Task<ScreenTextCaptureResult> CaptureAsync(
        int maxCharacters,
        int maxPayloadBytes,
        CancellationToken cancellationToken) =>
        _engine.CaptureAsync(maxCharacters, maxPayloadBytes, cancellationToken);

    private static async Task<ScreenRegionCapture?> CaptureRegionAsync(
        CancellationToken cancellationToken)
    {
        var monitors = Native.GetMonitors();
        if (monitors.Count == 0)
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
            foreach (var monitor in monitors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bitmap = new Bitmap(
                    monitor.Bounds.Width,
                    monitor.Bounds.Height,
                    PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        monitor.Bounds.Location,
                        Point.Empty,
                        monitor.Bounds.Size,
                        CopyPixelOperation.SourceCopy);
                }

                var source = await CreateImageSourceAsync(bitmap);
                captures.Add(new MonitorCapture(monitor, bitmap));
                overlays.Add(new ScreenTextCaptureOverlay(
                    monitor.Bounds,
                    monitor.Scale,
                    source,
                    region => Complete(new SelectedRegion(monitor.Bounds, region)),
                    () => Complete(null)));
            }

            using var registration = cancellationToken.Register(() =>
            {
                var dispatcher = overlays.FirstOrDefault()?.DispatcherQueue;
                _ = dispatcher?.TryEnqueue(() => Complete(null));
            });
            foreach (var overlay in overlays)
            {
                overlay.AppWindow.Show(activateWindow: false);
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

            var capture = captures.Single(item =>
                item.Monitor.Bounds == selected.MonitorBounds);
            captures.Remove(capture);
            var local = selected.Region;
            local.Offset(-capture.Monitor.Bounds.X, -capture.Monitor.Bounds.Y);
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

    private static async Task<BitmapImage> CreateImageSourceAsync(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        using var randomAccess = stream.AsRandomAccessStream();
        var source = new BitmapImage();
        await source.SetSourceAsync(randomAccess);
        return source;
    }

    private sealed record MonitorCapture(ScreenMonitor Monitor, Bitmap Bitmap);
    private sealed record SelectedRegion(Rectangle MonitorBounds, Rectangle Region);
}

public sealed record ScreenMonitor(Rectangle Bounds, double Scale);

internal static class Native
{
    public static IReadOnlyList<ScreenMonitor> GetMonitors()
    {
        var monitors = new List<ScreenMonitor>();
        _ = EnumDisplayMonitors(0, 0, (
            nint monitor,
            nint deviceContext,
            ref Rect rect,
            nint data) =>
        {
            var scale = GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0
                ? dpiX / 96d
                : 1d;
            monitors.Add(new ScreenMonitor(
                Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom),
                scale));
            return true;
        }, 0);
        return monitors;
    }

    private delegate bool MonitorEnumProc(
        nint monitor,
        nint deviceContext,
        ref Rect rect,
        nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        nint deviceContext,
        nint clip,
        MonitorEnumProc callback,
        nint data);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        nint monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);
}
