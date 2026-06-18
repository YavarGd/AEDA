namespace PersonalAI.Core.Voice;

public enum AudioPlaybackStatus
{
    Started,
    Completed,
    Paused,
    Stopped,
    Cancelled,
    Failed
}

public sealed record AudioPlaybackOptions(
    string? OutputDeviceId = null);

public sealed record AudioPlaybackResult(
    AudioPlaybackStatus Status,
    TimeSpan Duration = default,
    string? SafeErrorCode = null);

public interface IAudioPlaybackSession
{
    ValueTask<AudioPlaybackResult> StopAsync(
        CancellationToken cancellationToken = default);

    ValueTask<AudioPlaybackResult> WaitForCompletionAsync(
        CancellationToken cancellationToken = default);
}

public interface IAudioPlaybackService
{
    ValueTask<IAudioPlaybackSession> PlayAsync(
        Stream audio,
        SpeechAudioFormat format,
        AudioPlaybackOptions options,
        CancellationToken cancellationToken = default);
}
