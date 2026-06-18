namespace PersonalAI.Core.Voice;

public sealed record TextToSpeechResult(
    bool IsSuccess,
    SpeechAudioFormat Format,
    TimeSpan Duration,
    Stream? Audio = null,
    string? SafeErrorCode = null);
