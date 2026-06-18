using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workers;

namespace PersonalAI.Core.Voice;

public sealed class LocalTextToSpeechWorkerProvider : ITextToSpeechProvider
{
    private readonly string _workerId;
    private readonly IVoiceWorkerClient _workerClient;
    private readonly ILocalWorkerSupervisor _workerSupervisor;
    private readonly TimeSpan _defaultTimeout;
    private readonly ITaskRuntime? _taskRuntime;
    private readonly TaskId? _taskId;

    public LocalTextToSpeechWorkerProvider(
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

    public string ProviderId => "local-worker-tts";

    public bool IsLocalOnly => true;

    public async ValueTask<IReadOnlyList<VoiceInfo>> ListVoicesAsync(
        CancellationToken cancellationToken = default)
    {
        var health = await _workerSupervisor.GetHealthAsync(_workerId, cancellationToken);
        if (health.Status != LocalWorkerStatus.Running)
        {
            return [];
        }

        try
        {
            return await _workerClient.ListVoicesAsync(_workerId, cancellationToken);
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async ValueTask<TextToSpeechResult> SynthesizeAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Failed("text_required", request.Format);
        }

        if (request.Text.Length > VoiceWorkerProtocol.MaxTextLength)
        {
            return Failed("text_too_large", request.Format);
        }

        await AppendAsync(TaskEventKind.SpeechSynthesisStarted, "Speech synthesis started.", cancellationToken);

        var health = await _workerSupervisor.GetHealthAsync(_workerId, cancellationToken);
        if (health.Status != LocalWorkerStatus.Running)
        {
            await AppendAsync(TaskEventKind.SpeechFailed, "Speech failed.", cancellationToken);
            return Failed(health.SafeErrorCode ?? "worker_unavailable", request.Format);
        }

        using var timeout = new CancellationTokenSource(_defaultTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            var result = await _workerClient.SynthesizeAsync(
                _workerId,
                new VoiceWorkerSynthesizeRequest(
                    request.Text,
                    request.VoiceId,
                    request.SpeakingRate,
                    request.Format,
                    _defaultTimeout),
                linked.Token);

            if (!result.IsSuccess)
            {
                await AppendAsync(TaskEventKind.SpeechFailed, "Speech failed.", cancellationToken);
                return Failed(result.SafeErrorCode, result.Format);
            }

            await AppendAsync(TaskEventKind.SpeechSynthesisCompleted, "Speech synthesis completed.", cancellationToken);
            return new TextToSpeechResult(
                true,
                result.Format,
                result.Duration,
                new MemoryStream(result.AudioBytes, writable: false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopAsync(CancellationToken.None);
            await AppendAsync(TaskEventKind.VoiceCancelled, "Voice operation cancelled.", CancellationToken.None);
            return Failed("operation_cancelled", request.Format);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(CancellationToken.None);
            await AppendAsync(TaskEventKind.SpeechFailed, "Speech failed.", CancellationToken.None);
            return Failed("operation_timeout", request.Format);
        }
        catch (Exception)
        {
            await AppendAsync(TaskEventKind.SpeechFailed, "Speech failed.", CancellationToken.None);
            return Failed("worker_failure", request.Format);
        }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default) =>
        _workerClient.CancelAsync(
            _workerId,
            new VoiceWorkerCancelRequest("synthesis"),
            cancellationToken);

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

    private static TextToSpeechResult Failed(
        string? safeErrorCode,
        SpeechAudioFormat? format) =>
        new(
            false,
            format ?? new SpeechAudioFormat("pcm16", 16000, 1),
            TimeSpan.Zero,
            Audio: null,
            SafeErrorCode: TaskEventMetadata.SanitizeErrorCode(safeErrorCode) ??
                "voice_worker_error");

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
}
