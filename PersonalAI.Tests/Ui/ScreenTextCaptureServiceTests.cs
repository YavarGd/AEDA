using System.Drawing;
using PersonalAI.Infrastructure.ScreenCapture;

namespace PersonalAI.Tests.Ui;

/// <summary>
/// Restored after the WinUI desktop project's retirement. The behavior under test lives
/// entirely in <see cref="ScreenTextCaptureEngine"/> and friends in
/// PersonalAI.Infrastructure.ScreenCapture, which the Avalonia capture pipeline
/// (AvaloniaScreenTextCaptureService) still uses unchanged. Only the overlay window that
/// drove the drag gesture was WinUI-specific and was retired with it.
/// </summary>
public sealed class ScreenTextCaptureServiceTests
{
    [Theory]
    [InlineData(1.0, 10, 20, 90, 80)]
    [InlineData(1.25, 12, 25, 113, 100)]
    [InlineData(1.5, 15, 30, 135, 120)]
    [InlineData(2.0, 20, 40, 180, 160)]
    public void DragCoordinatesConvertDipToPhysicalPixels(
        double scale,
        int expectedX,
        int expectedY,
        int expectedWidth,
        int expectedHeight)
    {
        var region = ScreenRegionGeometry.FromDrag(
            new Rectangle(0, 0, 300, 300),
            scale,
            new PointF(10, 20),
            new PointF(100, 100));

        Assert.Equal(new Rectangle(
            expectedX,
            expectedY,
            expectedWidth,
            expectedHeight), region);
    }

    [Theory]
    [InlineData(10, 20, 100, 100)]
    [InlineData(100, 20, 10, 100)]
    [InlineData(10, 100, 100, 20)]
    [InlineData(100, 100, 10, 20)]
    public void DragDirectionIsNormalized(float x1, float y1, float x2, float y2)
    {
        var region = ScreenRegionGeometry.FromDrag(
            new Rectangle(0, 0, 200, 200),
            1,
            new PointF(x1, y1),
            new PointF(x2, y2));

        Assert.Equal(new Rectangle(10, 20, 90, 80), region);
    }

    [Fact]
    public void NegativeMonitorCoordinatesAndClampingArePreserved()
    {
        var region = ScreenRegionGeometry.FromDrag(
            new Rectangle(-1920, -200, 1920, 1080),
            1.5,
            new PointF(-20, -10),
            new PointF(1400, 900));

        Assert.Equal(new Rectangle(-1920, -200, 1920, 1080), region);
    }

    [Fact]
    public void TinyRegionIsRejected()
    {
        Assert.Null(ScreenRegionGeometry.FromDrag(
            new Rectangle(0, 0, 100, 100),
            2,
            new PointF(10, 10),
            new PointF(15, 15)));
    }

    [Fact]
    public void EmptyOrInvalidMonitorBoundsAndScaleAreRejected()
    {
        Assert.Null(ScreenRegionGeometry.FromDrag(
            new Rectangle(0, 0, 0, 0),
            1,
            new PointF(0, 0),
            new PointF(50, 50)));
        Assert.Null(ScreenRegionGeometry.FromDrag(
            new Rectangle(0, 0, 200, 200),
            0,
            new PointF(0, 0),
            new PointF(50, 50)));
        Assert.Null(ScreenRegionGeometry.FromDrag(
            new Rectangle(0, 0, 200, 200),
            -1,
            new PointF(0, 0),
            new PointF(50, 50)));
    }

    [Fact]
    public async Task SuccessfulRecognitionUsesCropAndDisposesBitmaps()
    {
        var source = new Bitmap(100, 80);
        var recognizer = new FakeRecognizer(new(
            ScreenTextCaptureStatus.Success,
            "recognized"));
        var engine = new ScreenTextCaptureEngine(
            recognizer,
            _ => Task.FromResult<ScreenRegionCapture?>(new(
                source,
                new Rectangle(10, 20, 40, 30))));

        var result = await engine.CaptureAsync(
            1_000,
            ScreenTextCaptureEngine.DefaultMaxPayloadBytes,
            CancellationToken.None);

        Assert.Equal(ScreenTextCaptureStatus.Success, result.Status);
        Assert.Equal(new Size(40, 30), recognizer.ReceivedSize);
        Assert.ThrowsAny<Exception>(() => _ = source.Width);
        Assert.True(recognizer.InputDisposedAfterReturn);
    }

    [Theory]
    [InlineData(ScreenTextCaptureStatus.NoText)]
    [InlineData(ScreenTextCaptureStatus.OcrUnavailable)]
    [InlineData(ScreenTextCaptureStatus.TimedOut)]
    [InlineData(ScreenTextCaptureStatus.Failed)]
    public async Task RecognitionFailureIsReturnedWithoutFiles(
        ScreenTextCaptureStatus status)
    {
        var engine = new ScreenTextCaptureEngine(
            new FakeRecognizer(new(status)),
            _ => Task.FromResult<ScreenRegionCapture?>(new(
                new Bitmap(20, 20),
                new Rectangle(0, 0, 20, 20))));

        var result = await engine.CaptureAsync(
            1_000,
            ScreenTextCaptureEngine.DefaultMaxPayloadBytes,
            CancellationToken.None);

        Assert.Equal(status, result.Status);
    }

    [Fact]
    public async Task CancelledOverlayReturnsCleanly()
    {
        var engine = new ScreenTextCaptureEngine(
            new FakeRecognizer(new(ScreenTextCaptureStatus.Success, "unused")),
            _ => Task.FromResult<ScreenRegionCapture?>(null));

        var result = await engine.CaptureAsync(
            1_000,
            ScreenTextCaptureEngine.DefaultMaxPayloadBytes,
            CancellationToken.None);

        Assert.Equal(ScreenTextCaptureStatus.Cancelled, result.Status);
    }

    [Fact]
    public void OcrTextNormalizationBoundsLinesAndCharacters()
    {
        var lines = Enumerable.Range(0, 2_100).Select(index => $"  line {index}  ");

        var value = ScreenTextNormalizer.Normalize(lines, 75);

        Assert.NotNull(value);
        Assert.True(value.Length <= 75);
        Assert.DoesNotContain("  ", value);
        Assert.Null(ScreenTextNormalizer.Normalize([" ", null], 100));
    }

    [Fact]
    public void OcrTextNormalizationPreservesLinesAndCapsLineCount()
    {
        var lines = Enumerable.Range(0, 2_100).Select(index => $"line {index}");

        var value = ScreenTextNormalizer.Normalize(lines, 100_000);

        Assert.NotNull(value);
        Assert.Contains($"line 0{Environment.NewLine}line 1", value);
        Assert.Contains("line 1999", value);
        Assert.DoesNotContain("line 2000", value);
    }

    [Fact]
    public async Task CancellationStopsCaptureAndDoesNotRunRecognizer()
    {
        var recognizer = new FakeRecognizer(new(
            ScreenTextCaptureStatus.Success,
            "unused"));
        var engine = new ScreenTextCaptureEngine(
            recognizer,
            async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return null;
            });
        using var cancellation = new CancellationTokenSource();

        var capture = engine.CaptureAsync(
            1_000,
            ScreenTextCaptureEngine.DefaultMaxPayloadBytes,
            cancellation.Token);
        cancellation.Cancel();
        var result = await capture;

        Assert.Equal(ScreenTextCaptureStatus.Cancelled, result.Status);
        Assert.Equal(Size.Empty, recognizer.ReceivedSize);
    }

    [Fact]
    public async Task ConcurrentCaptureReturnsBusy()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new ScreenTextCaptureEngine(
            new FakeRecognizer(new(ScreenTextCaptureStatus.Success, "unused")),
            async _ =>
            {
                started.TrySetResult();
                await release.Task;
                return null;
            });

        var first = engine.CaptureAsync(
            1_000,
            ScreenTextCaptureEngine.DefaultMaxPayloadBytes,
            CancellationToken.None);
        await started.Task;
        var second = await engine.CaptureAsync(
            1_000,
            ScreenTextCaptureEngine.DefaultMaxPayloadBytes,
            CancellationToken.None);
        release.TrySetResult();
        await first;

        Assert.Equal(ScreenTextCaptureStatus.Busy, second.Status);
    }

    [Fact]
    public async Task InvalidCropBoundsAreRejected()
    {
        var recognizer = new FakeRecognizer(new(
            ScreenTextCaptureStatus.Success,
            "unused"));
        var engine = new ScreenTextCaptureEngine(
            recognizer,
            _ => Task.FromResult<ScreenRegionCapture?>(new(
                new Bitmap(20, 20),
                new Rectangle(-1, 0, 20, 20))));

        var result = await engine.CaptureAsync(
            1_000,
            ScreenTextCaptureEngine.DefaultMaxPayloadBytes,
            CancellationToken.None);

        Assert.Equal(ScreenTextCaptureStatus.Failed, result.Status);
        Assert.Equal(Size.Empty, recognizer.ReceivedSize);
    }

    [Fact]
    public async Task PayloadLimitRejectsOversizedCrop()
    {
        var recognizer = new FakeRecognizer(new(
            ScreenTextCaptureStatus.Success,
            "unused"));
        var engine = new ScreenTextCaptureEngine(
            recognizer,
            _ => Task.FromResult<ScreenRegionCapture?>(new(
                new Bitmap(20, 20),
                new Rectangle(0, 0, 20, 20))));

        var result = await engine.CaptureAsync(
            1_000,
            maxPayloadBytes: 1_599,
            CancellationToken.None);

        Assert.Equal(ScreenTextCaptureStatus.Failed, result.Status);
        Assert.Contains("too large", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Size.Empty, recognizer.ReceivedSize);
    }

    [Fact]
    public async Task OcrTimeoutReturnsSafeStatus()
    {
        var engine = new ScreenTextCaptureEngine(
            new WaitingRecognizer(),
            _ => Task.FromResult<ScreenRegionCapture?>(new(
                new Bitmap(20, 20),
                new Rectangle(0, 0, 20, 20))),
            TimeSpan.FromMilliseconds(20));

        var result = await engine.CaptureAsync(
            1_000,
            ScreenTextCaptureEngine.DefaultMaxPayloadBytes,
            CancellationToken.None);

        Assert.Equal(ScreenTextCaptureStatus.TimedOut, result.Status);
    }

    [Fact]
    public async Task OcrCancellationDuringRecognitionStopsCleanly()
    {
        var recognizer = new CancellationObservingRecognizer();
        var engine = new ScreenTextCaptureEngine(
            recognizer,
            _ => Task.FromResult<ScreenRegionCapture?>(new(
                new Bitmap(20, 20),
                new Rectangle(0, 0, 20, 20))));
        using var cancellation = new CancellationTokenSource();

        var capture = engine.CaptureAsync(
            1_000,
            ScreenTextCaptureEngine.DefaultMaxPayloadBytes,
            cancellation.Token);
        await recognizer.Started.Task;
        cancellation.Cancel();
        var result = await capture;

        Assert.Equal(ScreenTextCaptureStatus.Cancelled, result.Status);
    }

    private sealed class FakeRecognizer(ScreenTextCaptureResult result) :
        IWindowsOcrTextRecognizer
    {
        private Bitmap? _input;
        public Size ReceivedSize { get; private set; }
        public bool InputDisposedAfterReturn => AssertDisposed(_input);

        public Task<ScreenTextCaptureResult> RecognizeAsync(
            Bitmap bitmap,
            int maxCharacters,
            CancellationToken cancellationToken)
        {
            _input = bitmap;
            ReceivedSize = bitmap.Size;
            return Task.FromResult(result);
        }

        private static bool AssertDisposed(Bitmap? bitmap)
        {
            try
            {
                _ = bitmap?.Width;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    private sealed class WaitingRecognizer : IWindowsOcrTextRecognizer
    {
        public async Task<ScreenTextCaptureResult> RecognizeAsync(
            Bitmap bitmap,
            int maxCharacters,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new(ScreenTextCaptureStatus.Success, "unused");
        }
    }

    private sealed class CancellationObservingRecognizer : IWindowsOcrTextRecognizer
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ScreenTextCaptureResult> RecognizeAsync(
            Bitmap bitmap,
            int maxCharacters,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new(ScreenTextCaptureStatus.Success, "unused");
        }
    }
}
