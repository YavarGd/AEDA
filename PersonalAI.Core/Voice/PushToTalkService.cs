using PersonalAI.Core.Tasks;

namespace PersonalAI.Core.Voice;

public sealed record PushToTalkStartRequest(
    AudioCaptureOptions CaptureOptions,
    TaskId? TaskId = null);

public sealed record PushToTalkStartResult(
    bool IsSuccess,
    TaskId TaskId,
    string? SafeErrorCode = null);

public sealed record PushToTalkTranscriptionResult(
    bool IsSuccess,
    TaskId TaskId,
    string Transcript,
    string? SafeErrorCode = null);

public interface IPushToTalkService
{
    ValueTask<PushToTalkStartResult> StartListeningAsync(
        PushToTalkStartRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PushToTalkTranscriptionResult> StopAndTranscribeAsync(
        string? languageHint = null,
        CancellationToken cancellationToken = default);

    ValueTask CancelAsync(CancellationToken cancellationToken = default);
}

public sealed class PushToTalkService : IPushToTalkService
{
    private readonly IAudioCaptureService _captureService;
    private readonly ISpeechToTextProvider _speechToTextProvider;
    private readonly ITaskRuntime _taskRuntime;
    private readonly object _gate = new();
    private ActiveSession? _activeSession;

    public PushToTalkService(
        IAudioCaptureService captureService,
        ISpeechToTextProvider speechToTextProvider,
        ITaskRuntime taskRuntime)
    {
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _speechToTextProvider = speechToTextProvider ?? throw new ArgumentNullException(nameof(speechToTextProvider));
        _taskRuntime = taskRuntime ?? throw new ArgumentNullException(nameof(taskRuntime));
    }

    public async ValueTask<PushToTalkStartResult> StartListeningAsync(
        PushToTalkStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_activeSession is not null)
            {
                return new PushToTalkStartResult(false, _activeSession.TaskId, "voice_session_active");
            }
        }

        var taskId = request.TaskId;
        if (taskId is null)
        {
            var taskRun = await _taskRuntime.StartTaskAsync(
                "Push-to-talk",
                "voice",
                cancellationToken: cancellationToken);
            taskId = taskRun.Id;
        }

        try
        {
            var captureSession = await _captureService.StartCaptureAsync(
                request.CaptureOptions,
                cancellationToken);
            await _taskRuntime.AppendEventAsync(
                taskId.Value,
                TaskEventKind.VoiceCaptureStarted,
                "Voice capture started.",
                cancellationToken);

            lock (_gate)
            {
                if (_activeSession is not null)
                {
                    _ = captureSession.CancelAsync(CancellationToken.None);
                    return new PushToTalkStartResult(false, _activeSession.TaskId, "voice_session_active");
                }

                _activeSession = new ActiveSession(taskId.Value, captureSession);
            }

            return new PushToTalkStartResult(true, taskId.Value);
        }
        catch (OperationCanceledException)
        {
            await _taskRuntime.AppendEventAsync(
                taskId.Value,
                TaskEventKind.VoiceCancelled,
                "Voice operation cancelled.",
                CancellationToken.None);
            return new PushToTalkStartResult(false, taskId.Value, "operation_cancelled");
        }
        catch (Exception)
        {
            await _taskRuntime.AppendEventAsync(
                taskId.Value,
                TaskEventKind.TranscriptionFailed,
                "Transcription failed.",
                CancellationToken.None);
            return new PushToTalkStartResult(false, taskId.Value, "capture_failed");
        }
    }

    public async ValueTask<PushToTalkTranscriptionResult> StopAndTranscribeAsync(
        string? languageHint = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = TakeActiveSession();
        if (active is null)
        {
            return new PushToTalkTranscriptionResult(false, default, string.Empty, "no_active_voice_session");
        }

        try
        {
            var captureResult = await active.CaptureSession.StopAsync(cancellationToken);
            await _taskRuntime.AppendEventAsync(
                active.TaskId,
                TaskEventKind.VoiceCaptureStopped,
                "Voice capture stopped.",
                cancellationToken);

            if (captureResult.Status != AudioCaptureStatus.Completed ||
                string.IsNullOrWhiteSpace(captureResult.AudioFilePath))
            {
                await _taskRuntime.AppendEventAsync(
                    active.TaskId,
                    TaskEventKind.TranscriptionFailed,
                    "Transcription failed.",
                    cancellationToken);
                return new PushToTalkTranscriptionResult(
                    false,
                    active.TaskId,
                    string.Empty,
                    captureResult.SafeErrorCode ?? "capture_failed");
            }

            await _taskRuntime.AppendEventAsync(
                active.TaskId,
                TaskEventKind.TranscriptionStarted,
                "Transcription started.",
                cancellationToken);
            var transcription = await _speechToTextProvider.RecognizeAsync(
                new SpeechRecognitionRequest(
                    AudioFilePath: captureResult.AudioFilePath,
                    LanguageHint: languageHint,
                    IsPushToTalk: true),
                cancellationToken);

            if (transcription.Status == SpeechRecognitionStatus.Completed)
            {
                await _taskRuntime.AppendEventAsync(
                    active.TaskId,
                    TaskEventKind.TranscriptionCompleted,
                    "Transcription completed.",
                    cancellationToken);
                return new PushToTalkTranscriptionResult(
                    true,
                    active.TaskId,
                    transcription.Transcript);
            }

            var kind = transcription.Status == SpeechRecognitionStatus.Cancelled
                ? TaskEventKind.VoiceCancelled
                : TaskEventKind.TranscriptionFailed;
            await _taskRuntime.AppendEventAsync(
                active.TaskId,
                kind,
                kind == TaskEventKind.VoiceCancelled
                    ? "Voice operation cancelled."
                    : "Transcription failed.",
                cancellationToken);
            return new PushToTalkTranscriptionResult(
                false,
                active.TaskId,
                string.Empty,
                transcription.SafeErrorCode ?? "transcription_failed");
        }
        catch (OperationCanceledException)
        {
            await active.CaptureSession.CancelAsync(CancellationToken.None);
            await _taskRuntime.AppendEventAsync(
                active.TaskId,
                TaskEventKind.VoiceCancelled,
                "Voice operation cancelled.",
                CancellationToken.None);
            return new PushToTalkTranscriptionResult(
                false,
                active.TaskId,
                string.Empty,
                "operation_cancelled");
        }
        finally
        {
            await active.CaptureSession.DisposeAsync();
        }
    }

    public async ValueTask CancelAsync(CancellationToken cancellationToken = default)
    {
        var active = TakeActiveSession();
        if (active is null)
        {
            return;
        }

        await active.CaptureSession.CancelAsync(cancellationToken);
        await _taskRuntime.AppendEventAsync(
            active.TaskId,
            TaskEventKind.VoiceCancelled,
            "Voice operation cancelled.",
            cancellationToken);
        await active.CaptureSession.DisposeAsync();
    }

    private ActiveSession? TakeActiveSession()
    {
        lock (_gate)
        {
            var active = _activeSession;
            _activeSession = null;
            return active;
        }
    }

    private sealed record ActiveSession(
        TaskId TaskId,
        IAudioCaptureSession CaptureSession);
}
