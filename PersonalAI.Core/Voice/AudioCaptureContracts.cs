namespace PersonalAI.Core.Voice;

public enum AudioCaptureStatus
{
    Started,
    Completed,
    Cancelled,
    Failed
}

public sealed record AudioCaptureOptions(
    TimeSpan MaxDuration,
    int SampleRate = 16000,
    int ChannelCount = 1,
    string? MicrophoneDeviceId = null,
    bool DeleteTemporaryAudioOnDispose = true);

public sealed record AudioCaptureResult(
    AudioCaptureStatus Status,
    string? AudioFilePath = null,
    SpeechAudioFormat? Format = null,
    TimeSpan Duration = default,
    string? SafeErrorCode = null);

public interface IAudioCaptureSession : IAsyncDisposable
{
    AudioCaptureOptions Options { get; }

    ValueTask<AudioCaptureResult> StopAsync(
        CancellationToken cancellationToken = default);

    ValueTask<AudioCaptureResult> CancelAsync(
        CancellationToken cancellationToken = default);
}

public interface IAudioCaptureService
{
    ValueTask<IAudioCaptureSession> StartCaptureAsync(
        AudioCaptureOptions options,
        CancellationToken cancellationToken = default);
}
