using PersonalAI.Core.Voice;

namespace PersonalAI.Tests.Voice;

public sealed class VoiceContractsTests
{
    [Fact]
    public async Task SpeechToTextFake_ReturnsTranscriptAndSupportsCancellation()
    {
        var provider = new FakeSpeechToTextProvider();

        var result = await provider.RecognizeAsync(
            new SpeechRecognitionRequest(IsPushToTalk: true));

        Assert.Equal(SpeechRecognitionStatus.Completed, result.Status);
        Assert.Equal("hello world", result.Transcript);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await provider.RecognizeAsync(
                new SpeechRecognitionRequest(),
                cancellation.Token));
    }

    [Fact]
    public async Task TextToSpeechFake_ReturnsVoicesAudioMetadataAndCancellation()
    {
        var provider = new FakeTextToSpeechProvider();

        var voices = await provider.ListVoicesAsync();
        var result = await provider.SynthesizeAsync(
            new TextToSpeechRequest("hello", VoiceId: "local"));

        Assert.Single(voices);
        Assert.True(result.IsSuccess);
        Assert.Equal(16000, result.Format.SampleRate);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await provider.SynthesizeAsync(
                new TextToSpeechRequest("cancel"),
                cancellation.Token));
    }

    [Fact]
    public void VoiceSettings_DefaultsToLocalOnlyAndNoRecordingState()
    {
        var settings = VoiceSettings.CreateDefault();

        Assert.True(settings.LocalOnly);
        Assert.Null(settings.MicrophoneDeviceId);
        Assert.Null(settings.SpeechToTextProviderId);
    }

    private sealed class FakeSpeechToTextProvider : ISpeechToTextProvider
    {
        public string ProviderId => "fake-stt";

        public bool IsLocalOnly => true;

        public bool SupportsStreaming => true;

        public ValueTask<SpeechRecognitionResult> RecognizeAsync(
            SpeechRecognitionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new SpeechRecognitionResult(
                SpeechRecognitionStatus.Completed,
                "hello world",
                [
                    new SpeechRecognitionSegment(
                        "hello world",
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(1),
                        0.9)
                ]));
        }
    }

    private sealed class FakeTextToSpeechProvider : ITextToSpeechProvider
    {
        public string ProviderId => "fake-tts";

        public bool IsLocalOnly => true;

        public ValueTask<IReadOnlyList<VoiceInfo>> ListVoicesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<VoiceInfo>>(
                [new VoiceInfo("local", "Local Voice")]);
        }

        public ValueTask<TextToSpeechResult> SynthesizeAsync(
            TextToSpeechRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var format = request.Format ?? new SpeechAudioFormat("pcm16", 16000, 1);
            return ValueTask.FromResult(new TextToSpeechResult(
                true,
                format,
                TimeSpan.FromMilliseconds(250),
                new MemoryStream([1, 2, 3])));
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }
}
