namespace PersonalAI.Core.Context;

public sealed record ContextCaptureRequest(
    nint? WindowHandle = null,
    string? SelectedText = null,
    bool CaptureScreenshot = false);
