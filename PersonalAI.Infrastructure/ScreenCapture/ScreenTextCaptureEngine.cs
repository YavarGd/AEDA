#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;

namespace PersonalAI.Infrastructure.ScreenCapture;

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

public sealed class ScreenTextCaptureEngine(
    IWindowsOcrTextRecognizer recognizer,
    Func<CancellationToken, Task<ScreenRegionCapture?>> captureRegion,
    TimeSpan? recognitionTimeout = null)
{
    public const int DefaultMaxPayloadBytes = 4 * 1024 * 1024;
    private readonly TimeSpan _recognitionTimeout =
        recognitionTimeout ?? TimeSpan.FromSeconds(8);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ScreenTextCaptureResult> CaptureAsync(
        int maxCharacters,
        int maxPayloadBytes,
        CancellationToken cancellationToken)
    {
        var entered = false;
        try
        {
            entered = await _gate.WaitAsync(0, cancellationToken);
            if (!entered)
            {
                return new(
                    ScreenTextCaptureStatus.Busy,
                    Message: "Screen selection is already open.");
            }

            using var capture = await captureRegion(cancellationToken);
            if (capture is null)
            {
                return new(ScreenTextCaptureStatus.Cancelled);
            }

            if (!IsValid(capture))
            {
                return new(
                    ScreenTextCaptureStatus.Failed,
                    Message: "The selected screen area was invalid.");
            }

            using var cropped = capture.Bitmap.Clone(
                capture.Region,
                PixelFormat.Format32bppArgb);
            if (maxPayloadBytes <= 0 ||
                (long)cropped.Width * cropped.Height * 4 > maxPayloadBytes)
            {
                return new(
                    ScreenTextCaptureStatus.Failed,
                    Message: "The selected screen area was too large.");
            }

            using var recognitionCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            recognitionCancellation.CancelAfter(_recognitionTimeout);
            try
            {
                return await recognizer.RecognizeAsync(
                        cropped,
                        maxCharacters,
                        recognitionCancellation.Token)
                    .WaitAsync(recognitionCancellation.Token);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                return new(
                    ScreenTextCaptureStatus.TimedOut,
                    Message: "Screen text recognition timed out.");
            }
        }
        catch (OperationCanceledException)
        {
            return new(ScreenTextCaptureStatus.Cancelled);
        }
        catch
        {
            return new(
                ScreenTextCaptureStatus.Failed,
                Message: "Screen text capture failed.");
        }
        finally
        {
            if (entered)
            {
                _gate.Release();
            }
        }
    }

    private static bool IsValid(ScreenRegionCapture capture) =>
        capture.Region.Width > 0 &&
        capture.Region.Height > 0 &&
        capture.Region.Left >= 0 &&
        capture.Region.Top >= 0 &&
        capture.Region.Right <= capture.Bitmap.Width &&
        capture.Region.Bottom <= capture.Bitmap.Height;
}

public static class ScreenTextNormalizer
{
    public static string? Normalize(
        IEnumerable<string?> lines,
        int maxCharacters)
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
}
#endif
