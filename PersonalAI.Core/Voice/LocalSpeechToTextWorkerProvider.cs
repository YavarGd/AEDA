using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workers;

namespace PersonalAI.Core.Voice;

public sealed class LocalSpeechToTextWorkerProvider : ISpeechToTextProvider
{
    private readonly string _workerId;
    private readonly IVoiceWorkerClient _workerClient;
    private readonly ILocalWorkerSupervisor _workerSupervisor;
    private readonly TimeSpan _defaultTimeout;
    private readonly ITaskRuntime? _taskRuntime;
    private readonly TaskId? _taskId;

    public LocalSpeechToTextWorkerProvider(
        string workerId,
        IVoiceWorkerClient workerClient,
        ILocalWorkerSupervisor workerSupervisor,
        TimeSpan? defaultTimeout = null,
        ITaskRuntime? taskRuntime = null,
        TaskId? taskId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        _workerId = workerId;
        _workerClient = workerClient ?? throw new ArgumentNullException(nameof(workerClient));
        _workerSupervisor = workerSupervisor ?? throw new ArgumentNullException(nameof(workerSupervisor));
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
        _taskRuntime = taskRuntime;
        _taskId = taskId;
    }

    public string ProviderId => "local-worker-stt";

    public bool IsLocalOnly => true;

    public bool SupportsStreaming => false;

    public async ValueTask<SpeechRecognitionResult> RecognizeAsync(
        SpeechRecognitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.AudioFilePath))
        {
            return Failed("audio_file_required");
        }

        await AppendAsync(TaskEventKind.TranscriptionStarted, "Transcription started.", cancellationToken);

        var health = await _workerSupervisor.GetHealthAsync(_workerId, cancellationToken);
        if (health.Status != LocalWorkerStatus.Running)
        {
            await AppendAsync(TaskEventKind.TranscriptionFailed, "Transcription failed.", cancellationToken);
            return Failed(health.SafeErrorCode ?? "worker_unavailable");
        }

        using var timeout = new CancellationTokenSource(_defaultTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            var result = await _workerClient.TranscribeAsync(
                _workerId,
                new VoiceWorkerTranscribeRequest(
                    request.AudioFilePath,
                    LanguageHint: request.LanguageHint,
                    Timeout: _defaultTimeout),
                linked.Token);

            if (result.Status == SpeechRecognitionStatus.Completed ||
                result.Status == SpeechRecognitionStatus.Partial)
            {
                await AppendAsync(TaskEventKind.TranscriptionCompleted, "Transcription completed.", cancellationToken);
            }
            else if (result.Status == SpeechRecognitionStatus.Cancelled)
            {
                await AppendAsync(TaskEventKind.VoiceCancelled, "Voice operation cancelled.", cancellationToken);
            }
            else
            {
                await AppendAsync(TaskEventKind.TranscriptionFailed, "Transcription failed.", cancellationToken);
            }

            return new SpeechRecognitionResult(
                result.Status,
                result.Transcript,
                result.Segments,
                SafeError(result.SafeErrorCode));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryCancelWorkerAsync();
            await AppendAsync(TaskEventKind.VoiceCancelled, "Voice operation cancelled.", CancellationToken.None);
            return new SpeechRecognitionResult(
                SpeechRecognitionStatus.Cancelled,
                string.Empty,
                [],
                "operation_cancelled");
        }
        catch (OperationCanceledException)
        {
            await TryCancelWorkerAsync();
            await AppendAsync(TaskEventKind.TranscriptionFailed, "Transcription failed.", CancellationToken.None);
            return Failed("operation_timeout");
        }
        catch (Exception)
        {
            await AppendAsync(TaskEventKind.TranscriptionFailed, "Transcription failed.", CancellationToken.None);
            return Failed("worker_failure");
        }
    }

    public async ValueTask<VoiceWorkerHealthCheckResult> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var supervisorHealth = await _workerSupervisor.GetHealthAsync(
            _workerId,
            cancellationToken);
        if (supervisorHealth.Status != LocalWorkerStatus.Running)
        {
            return new VoiceWorkerHealthCheckResult(
                supervisorHealth.Status,
                VoiceWorkerProtocol.CurrentVersion,
                [],
                supervisorHealth.SafeErrorCode ?? "worker_unavailable");
        }

        return await _workerClient.CheckHealthAsync(_workerId, cancellationToken);
    }

    private static SpeechRecognitionResult Failed(string? safeErrorCode) =>
        new(
            SpeechRecognitionStatus.Failed,
            string.Empty,
            [],
            SafeError(safeErrorCode));

    private static string SafeError(string? safeErrorCode) =>
        TaskEventMetadata.SanitizeErrorCode(safeErrorCode) ?? "voice_worker_error";

    private async ValueTask AppendAsync(
        TaskEventKind kind,
        string summary,
        CancellationToken cancellationToken)
    {
        if (_taskRuntime is not null && _taskId is not null)
        {
            await _taskRuntime.AppendEventAsync(_taskId.Value, kind, summary, cancellationToken);
        }
    }

    private async ValueTask TryCancelWorkerAsync()
    {
        try
        {
            await _workerClient.CancelAsync(
                _workerId,
                new VoiceWorkerCancelRequest("transcription"),
                CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }
}
