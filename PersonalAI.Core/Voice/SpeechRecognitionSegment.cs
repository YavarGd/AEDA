namespace PersonalAI.Core.Voice;

public sealed record SpeechRecognitionSegment(
    string Text,
    TimeSpan Start,
    TimeSpan Duration,
    double? Confidence = null);
