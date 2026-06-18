namespace PersonalAI.Core.Voice;

public sealed record VoiceInfo(
    string Id,
    string DisplayName,
    string? Language = null,
    bool IsLocal = true);
