#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;

namespace PersonalAI.Infrastructure.Context;

public sealed class ScreenshotContextService(
    IActiveContextProvider activeContextProvider,
    ForegroundWindowTracker foregroundWindowTracker,
    Func<nint> getOwnWindowHandle,
    Func<ContextSettings>? getContextSettings = null)
{
    public const int MaxImageBytes = 4 * 1024 * 1024;
    private const int DefaultThumbnailMaxEdge = 240;

    public async Task<AttachedContextItem?> CaptureExternalWindowAsync(
        CancellationToken cancellationToken = default)
    {
        _ = foregroundWindowTracker.CaptureCurrentExternalWindow(getOwnWindowHandle());
        var externalWindow = foregroundWindowTracker.GetLastValidExternalWindow();

        if (externalWindow is null)
        {
            return null;
        }

        var context = await activeContextProvider.CaptureAsync(
            new ContextCaptureRequest(
                externalWindow.WindowHandle,
                SelectedText: null,
                CaptureScreenshot: true),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(context?.ScreenshotPath) ||
            !File.Exists(context.ScreenshotPath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(
            context.ScreenshotPath,
            cancellationToken);

        var settings = ApplicationSettingsValidator.NormalizeContext(
            getContextSettings?.Invoke() ?? ContextSettings.Default);

        if (bytes.Length > settings.ScreenshotMaxPayloadBytes)
        {
            DeleteTemporaryFile(context.ScreenshotPath);
            throw new InvalidOperationException(
                "Screenshot is too large to attach. Try a smaller window.");
        }

        using var image = Image.FromStream(new MemoryStream(bytes));
        var thumbnail = CreateThumbnailDataUri(
            image,
            settings.ScreenshotThumbnailMaxEdge);
        var title = string.IsNullOrWhiteSpace(context.WindowTitle)
            ? "Window screenshot"
            : context.WindowTitle.Trim();
        var sourceName = string.IsNullOrWhiteSpace(context.ProcessName)
            ? "Application window"
            : context.ProcessName.Trim();

        return AttachedContextFactory.FromScreenshot(
            title,
            sourceName,
            "Current window",
            image.Width,
            image.Height,
            "png",
            new ChatImage("image/png", Convert.ToBase64String(bytes)),
            thumbnail,
            context.ScreenshotPath,
            context.CapturedAtUtc);
    }

    public static void Release(AttachedContextItem item)
    {
        if (item.Metadata.TryGetValue("temporaryPath", out var path))
        {
            DeleteTemporaryFile(path);
        }
    }

    private static string CreateThumbnailDataUri(
        Image image,
        int thumbnailMaxEdge)
    {
        if (thumbnailMaxEdge <= 0)
        {
            thumbnailMaxEdge = DefaultThumbnailMaxEdge;
        }

        var scale = Math.Min(
            (double)thumbnailMaxEdge / image.Width,
            (double)thumbnailMaxEdge / image.Height);
        scale = Math.Min(1, scale);
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));

        using var thumbnail = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(thumbnail))
        {
            graphics.DrawImage(image, 0, 0, width, height);
        }

        using var stream = new MemoryStream();
        thumbnail.Save(stream, ImageFormat.Png);
        return $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException)
        {
        }
    }
}
#endif
