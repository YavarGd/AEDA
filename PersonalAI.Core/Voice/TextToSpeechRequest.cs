namespace PersonalAI.Core.Voice;

public sealed record TextToSpeechRequest(
    string Text,
    string? VoiceId = null,
    double SpeakingRate = 1.0,
    SpeechAudioFormat? Format = null);
