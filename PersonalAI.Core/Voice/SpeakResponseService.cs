using PersonalAI.Core.Tasks;

namespace PersonalAI.Core.Voice;

public sealed record SpeakResponseRequest(
    string Text,
    string? VoiceId = null,
    double SpeakingRate = 1.0,
    TaskId? TaskId = null);

public sealed record SpeakResponseResult(
    bool IsSuccess,
    TaskId TaskId,
    string? SafeErrorCode = null);

public interface ISpeakResponseService
{
    ValueTask<SpeakResponseResult> SpeakTextAsync(
        SpeakResponseRequest request,
        CancellationToken cancellationToken = default);

    ValueTask StopSpeakingAsync(CancellationToken cancellationToken = default);
}

public sealed class SpeakResponseService : ISpeakResponseService
{
    private readonly ITextToSpeechProvider _textToSpeechProvider;
    private readonly IAudioPlaybackService _playbackService;
    private readonly ITaskRuntime _taskRuntime;
    private IAudioPlaybackSession? _activePlayback;

    public SpeakResponseService(
        ITextToSpeechProvider textToSpeechProvider,
        IAudioPlaybackService playbackService,
        ITaskRuntime taskRuntime)
    {
        _textToSpeechProvider = textToSpeechProvider ?? throw new ArgumentNullException(nameof(textToSpeechProvider));
        _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        _taskRuntime = taskRuntime ?? throw new ArgumentNullException(nameof(taskRuntime));
    }

    public async ValueTask<SpeakResponseResult> SpeakTextAsync(
        SpeakResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var taskId = request.TaskId;
        if (taskId is null)
        {
            var taskRun = await _taskRuntime.StartTaskAsync(
                "Speak response",
                "voice",
                cancellationToken: cancellationToken);
            taskId = taskRun.Id;
        }

        try
        {
            await _taskRuntime.AppendEventAsync(
                taskId.Value,
                TaskEventKind.SpeechSynthesisStarted,
                "Speech synthesis started.",
                cancellationToken);
            var synthesis = await _textToSpeechProvider.SynthesizeAsync(
                new TextToSpeechRequest(
                    request.Text,
                    request.VoiceId,
                    request.SpeakingRate),
                cancellationToken);

            if (!synthesis.IsSuccess || synthesis.Audio is null)
            {
                await _taskRuntime.AppendEventAsync(
                    taskId.Value,
                    TaskEventKind.SpeechFailed,
                    "Speech failed.",
                    cancellationToken);
                return new SpeakResponseResult(
                    false,
                    taskId.Value,
                    synthesis.SafeErrorCode ?? "synthesis_failed");
            }

            await _taskRuntime.AppendEventAsync(
                taskId.Value,
                TaskEventKind.SpeechSynthesisCompleted,
                "Speech synthesis completed.",
                cancellationToken);
            await _taskRuntime.AppendEventAsync(
                taskId.Value,
                TaskEventKind.SpeechPlaybackStarted,
                "Speech playback started.",
                cancellationToken);
            _activePlayback = await _playbackService.PlayAsync(
                synthesis.Audio,
                synthesis.Format,
                new AudioPlaybackOptions(),
                cancellationToken);
            var playback = await _activePlayback.WaitForCompletionAsync(cancellationToken);
            _activePlayback = null;

            if (playback.Status == AudioPlaybackStatus.Completed)
            {
                await _taskRuntime.AppendEventAsync(
                    taskId.Value,
                    TaskEventKind.SpeechPlaybackCompleted,
                    "Speech playback completed.",
                    cancellationToken);
                return new SpeakResponseResult(true, taskId.Value);
            }

            await _taskRuntime.AppendEventAsync(
                taskId.Value,
                TaskEventKind.SpeechFailed,
                "Speech failed.",
                cancellationToken);
            return new SpeakResponseResult(
                false,
                taskId.Value,
                playback.SafeErrorCode ?? "playback_failed");
        }
        catch (OperationCanceledException)
        {
            if (_activePlayback is not null)
            {
                await _activePlayback.StopAsync(CancellationToken.None);
                _activePlayback = null;
            }

            await _textToSpeechProvider.StopAsync(CancellationToken.None);
            await _taskRuntime.AppendEventAsync(
                taskId.Value,
                TaskEventKind.VoiceCancelled,
                "Voice operation cancelled.",
                CancellationToken.None);
            return new SpeakResponseResult(false, taskId.Value, "operation_cancelled");
        }
        catch (Exception)
        {
            await _taskRuntime.AppendEventAsync(
                taskId.Value,
                TaskEventKind.SpeechFailed,
                "Speech failed.",
                CancellationToken.None);
            return new SpeakResponseResult(false, taskId.Value, "speech_failed");
        }
    }

    public async ValueTask StopSpeakingAsync(CancellationToken cancellationToken = default)
    {
        if (_activePlayback is not null)
        {
            await _activePlayback.StopAsync(cancellationToken);
            _activePlayback = null;
        }

        await _textToSpeechProvider.StopAsync(cancellationToken);
    }
}
