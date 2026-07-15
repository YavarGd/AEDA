using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media.Imaging;
using PersonalAI.Desktop.WinUI.Views;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace PersonalAI.Desktop.WinUI.Services;

public enum ScreenTextCaptureStatus
{
    Success,
    Cancelled,
    NoText,
    OcrUnavailable,
    TimedOut,
    Failed,
    Busy
}

public sealed record ScreenTextCaptureResult(
    ScreenTextCaptureStatus Status,
    string? Text = null,
    string? Message = null);

public interface IWindowsOcrTextRecognizer
{
    Task<ScreenTextCaptureResult> RecognizeAsync(
        Bitmap bitmap,
        int maxCharacters,
        CancellationToken cancellationToken);
}

public sealed record ScreenRegionCapture(Bitmap Bitmap, Rectangle Region) : IDisposable
{
    public void Dispose() => Bitmap.Dispose();
}

public static class ScreenRegionGeometry
{
    public static Rectangle? FromDrag(
        Rectangle monitorBounds,
        double rasterizationScale,
        PointF startDip,
        PointF endDip,
        double minimumDip = 8)
    {
        if (monitorBounds.Width <= 0 || monitorBounds.Height <= 0 ||
            rasterizationScale <= 0)
        {
            return null;
        }

        var left = (int)Math.Floor(Math.Min(startDip.X, endDip.X) * rasterizationScale) +
            monitorBounds.Left;
        var top = (int)Math.Floor(Math.Min(startDip.Y, endDip.Y) * rasterizationScale) +
            monitorBounds.Top;
        var right = (int)Math.Ceiling(Math.Max(startDip.X, endDip.X) * rasterizationScale) +
            monitorBounds.Left;
        var bottom = (int)Math.Ceiling(Math.Max(startDip.Y, endDip.Y) * rasterizationScale) +
            monitorBounds.Top;
        var normalized = Rectangle.FromLTRB(left, top, right, bottom);
        normalized.Intersect(monitorBounds);
        var minimumPixels = Math.Max(1, (int)Math.Ceiling(minimumDip * rasterizationScale));
        return normalized.Width >= minimumPixels && normalized.Height >= minimumPixels
            ? normalized
            : null;
    }
}

public sealed class ScreenTextCaptureService(
    IWindowsOcrTextRecognizer? recognizer = null,
    Func<CancellationToken, Task<ScreenRegionCapture?>>? captureRegion = null)
{
    private readonly IWindowsOcrTextRecognizer _recognizer =
        recognizer ?? new WindowsOcrTextRecognizer();
    private readonly Func<CancellationToken, Task<ScreenRegionCapture?>> _captureRegion =
        captureRegion ?? CaptureRegionAsync;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ScreenTextCaptureResult> CaptureAsync(
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return new(ScreenTextCaptureStatus.Busy, Message: "Screen selection is already open.");
        }

        try
        {
            using var capture = await _captureRegion(cancellationToken);
            if (capture is null)
            {
                return new(ScreenTextCaptureStatus.Cancelled);
            }

            if (capture.Region.Width <= 0 || capture.Region.Height <= 0 ||
                capture.Region.Left < 0 || capture.Region.Top < 0 ||
                capture.Region.Right > capture.Bitmap.Width ||
                capture.Region.Bottom > capture.Bitmap.Height)
            {
                return new(ScreenTextCaptureStatus.Failed, Message: "The selected screen area was invalid.");
            }

            using var cropped = capture.Bitmap.Clone(capture.Region, PixelFormat.Format32bppArgb);
            return await _recognizer.RecognizeAsync(
                cropped,
                maxCharacters,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new(ScreenTextCaptureStatus.Cancelled);
        }
        catch
        {
            return new(ScreenTextCaptureStatus.Failed, Message: "Screen text capture failed.");
        }
        finally
        {
            _gate.Release();
        }
    }

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

            var capture = captures.Single(item => item.Monitor.Bounds == selected.MonitorBounds);
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

public sealed class WindowsOcrTextRecognizer : IWindowsOcrTextRecognizer
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public async Task<ScreenTextCaptureResult> RecognizeAsync(
        Bitmap bitmap,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ??
            (OcrEngine.AvailableRecognizerLanguages
                .FirstOrDefault(language => language.LanguageTag.StartsWith(
                    "en", StringComparison.OrdinalIgnoreCase)) is { } english
                ? OcrEngine.TryCreateFromLanguage(english)
                : null);
        if (engine is null)
        {
            return new(
                ScreenTextCaptureStatus.OcrUnavailable,
                Message: "Install a Windows OCR language to select text on screen.");
        }

        Bitmap? prepared = null;
        try
        {
            prepared = Prepare(bitmap, checked((int)OcrEngine.MaxImageDimension));
            var source = prepared ?? bitmap;
            using var stream = new MemoryStream();
            source.Save(stream, ImageFormat.Bmp);
            stream.Position = 0;
            using var randomAccess = stream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(randomAccess);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore);
            var result = await engine.RecognizeAsync(softwareBitmap)
                .AsTask(cancellationToken)
                .WaitAsync(Timeout, cancellationToken);
            var text = Normalize(result.Lines.Select(line => line.Text), maxCharacters);
            return string.IsNullOrWhiteSpace(text)
                ? new(ScreenTextCaptureStatus.NoText, Message: "No text was found in that area.")
                : new(ScreenTextCaptureStatus.Success, text);
        }
        catch (TimeoutException)
        {
            return new(ScreenTextCaptureStatus.TimedOut, Message: "Screen text recognition timed out.");
        }
        finally
        {
            prepared?.Dispose();
        }
    }

    public static string? Normalize(IEnumerable<string?> lines, int maxCharacters)
    {
        if (maxCharacters <= 0)
        {
            return null;
        }

        var value = string.Join(
            Environment.NewLine,
            lines.Select(line => line?.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(2_000));
        value = value.Trim();
        return value.Length switch
        {
            0 => null,
            _ when value.Length > maxCharacters => value[..maxCharacters].TrimEnd(),
            _ => value
        };
    }

    private static Bitmap? Prepare(Bitmap bitmap, int maximumDimension)
    {
        var scale = Math.Min(
            1d,
            Math.Min(
                (double)maximumDimension / bitmap.Width,
                (double)maximumDimension / bitmap.Height));
        var width = Math.Max(64, (int)Math.Floor(bitmap.Width * scale));
        var height = Math.Max(64, (int)Math.Floor(bitmap.Height * scale));
        if (width == bitmap.Width && height == bitmap.Height)
        {
            return null;
        }

        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(
            bitmap,
            0,
            0,
            Math.Max(1, (int)Math.Floor(bitmap.Width * scale)),
            Math.Max(1, (int)Math.Floor(bitmap.Height * scale)));
        return result;
    }
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
