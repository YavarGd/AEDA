namespace PersonalAI.Core.Voice;

public sealed record SpeechRecognitionResult(
    SpeechRecognitionStatus Status,
    string Transcript,
    IReadOnlyList<SpeechRecognitionSegment> Segments,
    string? SafeErrorCode = null);
