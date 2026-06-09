namespace PersonalAI.Core.Context;

public sealed record ActiveWindowReference(
    nint WindowHandle,
    uint ProcessId,
    string? ProcessName,
    string? WindowTitle,
    DateTimeOffset CapturedAtUtc);
