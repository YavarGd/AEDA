namespace PersonalAI.Core.Voice;

public interface ITextToSpeechProvider
{
    string ProviderId { get; }

    bool IsLocalOnly { get; }

    ValueTask<IReadOnlyList<VoiceInfo>> ListVoicesAsync(
        CancellationToken cancellationToken = default);

    ValueTask<TextToSpeechResult> SynthesizeAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
