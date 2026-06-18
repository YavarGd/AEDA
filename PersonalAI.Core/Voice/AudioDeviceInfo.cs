namespace PersonalAI.Core.Voice;

public sealed record AudioDeviceInfo(
    string Id,
    string DisplayName,
    bool IsDefault = false);
