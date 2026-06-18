namespace PersonalAI.Core.Voice;

public sealed record SpeechRecognitionRequest(
    string? AudioFilePath = null,
    Stream? AudioStream = null,
    string? LanguageHint = null,
    bool IsPushToTalk = true);
