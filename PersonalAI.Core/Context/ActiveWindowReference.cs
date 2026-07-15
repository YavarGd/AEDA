namespace PersonalAI.Core.Context;

public sealed record ActiveWindowReference(
    nint WindowHandle,
    uint ProcessId,
    string? ProcessName,
    string? WindowTitle,
    DateTimeOffset CapturedAtUtc,
    GuiThreadWindowSnapshot? GuiThread = null);

public sealed record GuiThreadWindowSnapshot(
    uint ThreadId,
    uint ProcessId,
    nint ActiveWindow,
    nint FocusedWindow,
    nint CaptureWindow,
    nint MenuOwnerWindow,
    nint MoveSizeWindow,
    nint CaretWindow,
    DateTimeOffset CapturedAtUtc);
