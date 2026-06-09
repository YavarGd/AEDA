namespace PersonalAI.Core.Context;

public sealed record ActiveApplicationContext(
    nint? WindowHandle,
    uint? ProcessId,
    string? ProcessName,
    string? ExecutablePath,
    string? WindowTitle,
    string? CapturedSelectedText,
    string? ScreenshotPath,
    byte[]? ScreenshotBytes,
    DateTimeOffset CapturedAtUtc);
