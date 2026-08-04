namespace PersonalAI.Infrastructure.ScreenCapture;

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
