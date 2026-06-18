namespace PersonalAI.Core.Voice;

public interface ISpeechToTextProvider
{
    string ProviderId { get; }

    bool IsLocalOnly { get; }

    bool SupportsStreaming { get; }

    ValueTask<SpeechRecognitionResult> RecognizeAsync(
        SpeechRecognitionRequest request,
        CancellationToken cancellationToken = default);
}
