#if WINDOWS
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PersonalAI.Infrastructure.ScreenCapture;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace PersonalAI.Infrastructure.Windows;

public sealed class WindowsOcrTextRecognizer : IWindowsOcrTextRecognizer
{
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
                .AsTask(cancellationToken);
            var text = ScreenTextNormalizer.Normalize(
                result.Lines.Select(line => line.Text),
                maxCharacters);
            return string.IsNullOrWhiteSpace(text)
                ? new(
                    ScreenTextCaptureStatus.NoText,
                    Message: "No text was found in that area.")
                : new(ScreenTextCaptureStatus.Success, text);
        }
        finally
        {
            prepared?.Dispose();
        }
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
#endif
