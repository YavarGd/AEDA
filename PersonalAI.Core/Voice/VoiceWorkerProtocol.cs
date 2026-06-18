using PersonalAI.Core.Workers;

namespace PersonalAI.Core.Voice;

public static class VoiceWorkerProtocol
{
    public const string CurrentVersion = "voice-worker.v1";
    public const int MaxTextLength = 8_000;
    public const long MaxAudioBytes = 100 * 1024 * 1024;
}

public enum VoiceWorkerCapability
{
    SpeechToText,
    StreamingSpeechToText,
    TextToSpeech,
    VoiceListing,
    LanguageListing,
    Cancellation
}

public sealed record VoiceWorkerHealthCheckRequest(
    string ProtocolVersion = VoiceWorkerProtocol.CurrentVersion);

public sealed record VoiceWorkerHealthCheckResult(
    LocalWorkerStatus Status,
    string ProtocolVersion,
    IReadOnlyList<VoiceWorkerCapability> Capabilities,
    string? SafeErrorCode = null);

public sealed record VoiceWorkerLanguage(
    string Code,
    string DisplayName);

public sealed record VoiceWorkerTranscribeRequest(
    string AudioFilePath,
    SpeechAudioFormat? Format = null,
    string? LanguageHint = null,
    TimeSpan? Timeout = null,
    string ProtocolVersion = VoiceWorkerProtocol.CurrentVersion);

public sealed record VoiceWorkerTranscribeResult(
    SpeechRecognitionStatus Status,
    string Transcript,
    IReadOnlyList<SpeechRecognitionSegment> Segments,
    string? DetectedLanguage = null,
    string? SafeErrorCode = null);

public sealed record VoiceWorkerSynthesizeRequest(
    string Text,
    string? VoiceId = null,
    double SpeakingRate = 1.0,
    SpeechAudioFormat? Format = null,
    TimeSpan? Timeout = null,
    string ProtocolVersion = VoiceWorkerProtocol.CurrentVersion);

public sealed record VoiceWorkerSynthesizeResult(
    bool IsSuccess,
    SpeechAudioFormat Format,
    TimeSpan Duration,
    byte[] AudioBytes,
    string? SafeErrorCode = null);

public sealed record VoiceWorkerCancelRequest(
    string OperationId,
    string ProtocolVersion = VoiceWorkerProtocol.CurrentVersion);

public interface IVoiceWorkerClient
{
    ValueTask<VoiceWorkerHealthCheckResult> CheckHealthAsync(
        string workerId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<VoiceWorkerLanguage>> ListLanguagesAsync(
        string workerId,
        CancellationToken cancellationToken = default);

    ValueTask<VoiceWorkerTranscribeResult> TranscribeAsync(
        string workerId,
        VoiceWorkerTranscribeRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<VoiceInfo>> ListVoicesAsync(
        string workerId,
        CancellationToken cancellationToken = default);

    ValueTask<VoiceWorkerSynthesizeResult> SynthesizeAsync(
        string workerId,
        VoiceWorkerSynthesizeRequest request,
        CancellationToken cancellationToken = default);

    ValueTask CancelAsync(
        string workerId,
        VoiceWorkerCancelRequest request,
        CancellationToken cancellationToken = default);
}
