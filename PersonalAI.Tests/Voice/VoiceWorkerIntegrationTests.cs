using PersonalAI.Core.Tasks;
using PersonalAI.Core.Settings;
using PersonalAI.Core.Voice;
using PersonalAI.Core.Workers;
using PersonalAI.Infrastructure.Settings;

namespace PersonalAI.Tests.Voice;

public sealed class VoiceWorkerIntegrationTests
{
    [Fact]
    public async Task SttWorkerProvider_HealthAndTranscribeSuccess()
    {
        var worker = new FakeVoiceWorkerClient
        {
            TranscribeResult = new VoiceWorkerTranscribeResult(
                SpeechRecognitionStatus.Completed,
                "hello",
                [new SpeechRecognitionSegment("hello", TimeSpan.Zero, TimeSpan.FromSeconds(1), 0.9)])
        };
        var provider = new LocalSpeechToTextWorkerProvider(
            "stt",
            worker,
            new FakeWorkerSupervisor(LocalWorkerStatus.Running));

        var health = await provider.CheckHealthAsync();
        var result = await provider.RecognizeAsync(
            new SpeechRecognitionRequest(@"C:\tmp\audio.wav", LanguageHint: "en"));

        Assert.Equal(LocalWorkerStatus.Running, health.Status);
        Assert.Equal(SpeechRecognitionStatus.Completed, result.Status);
        Assert.Equal("hello", result.Transcript);
        Assert.Equal("en", worker.LastTranscribeRequest?.LanguageHint);
    }

    [Fact]
    public async Task SttWorkerProvider_UnavailableTimeoutAndFailureUseSafeCodes()
    {
        var unavailable = new LocalSpeechToTextWorkerProvider(
            "stt",
            new FakeVoiceWorkerClient(),
            new FakeWorkerSupervisor(LocalWorkerStatus.Unavailable, "worker_not_enabled"));
        var unavailableResult = await unavailable.RecognizeAsync(
            new SpeechRecognitionRequest(@"C:\secret\audio.wav"));

        var timeoutWorker = new FakeVoiceWorkerClient
        {
            TranscribeDelay = TimeSpan.FromSeconds(2)
        };
        var timeout = new LocalSpeechToTextWorkerProvider(
            "stt",
            timeoutWorker,
            new FakeWorkerSupervisor(LocalWorkerStatus.Running),
            TimeSpan.FromMilliseconds(10));
        var timeoutResult = await timeout.RecognizeAsync(
            new SpeechRecognitionRequest(@"C:\secret\audio.wav"));

        var failingWorker = new FakeVoiceWorkerClient
        {
            ThrowOnTranscribe = new InvalidOperationException("raw secret path")
        };
        var failing = new LocalSpeechToTextWorkerProvider(
            "stt",
            failingWorker,
            new FakeWorkerSupervisor(LocalWorkerStatus.Running));
        var failureResult = await failing.RecognizeAsync(
            new SpeechRecognitionRequest(@"C:\secret\audio.wav"));

        Assert.Equal("worker_not_enabled", unavailableResult.SafeErrorCode);
        Assert.Equal("operation_timeout", timeoutResult.SafeErrorCode);
        Assert.Equal("worker_failure", failureResult.SafeErrorCode);
        Assert.DoesNotContain("secret", failureResult.SafeErrorCode!);
    }

    [Fact]
    public async Task SttWorkerProvider_CancellationReturnsCancelledAndForwardsCancel()
    {
        var worker = new FakeVoiceWorkerClient { TranscribeDelay = TimeSpan.FromSeconds(2) };
        var provider = new LocalSpeechToTextWorkerProvider(
            "stt",
            worker,
            new FakeWorkerSupervisor(LocalWorkerStatus.Running));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await provider.RecognizeAsync(
            new SpeechRecognitionRequest(@"C:\tmp\audio.wav"),
            cancellation.Token);

        Assert.Equal(SpeechRecognitionStatus.Cancelled, result.Status);
        Assert.Equal("operation_cancelled", result.SafeErrorCode);
        Assert.True(worker.CancelCalled);
    }

    [Fact]
    public async Task TtsWorkerProvider_ListsVoicesAndSynthesizes()
    {
        var worker = new FakeVoiceWorkerClient();
        var provider = new LocalTextToSpeechWorkerProvider(
            "tts",
            worker,
            new FakeWorkerSupervisor(LocalWorkerStatus.Running));

        var voices = await provider.ListVoicesAsync();
        var result = await provider.SynthesizeAsync(
            new TextToSpeechRequest("hello", VoiceId: "local", SpeakingRate: 1.2));

        Assert.Single(voices);
        Assert.True(result.IsSuccess);
        Assert.Equal("local", worker.LastSynthesizeRequest?.VoiceId);
        Assert.Equal(1.2, worker.LastSynthesizeRequest?.SpeakingRate);
    }

    [Fact]
    public async Task TtsWorkerProvider_CancelTimeoutAndFailureAreSafe()
    {
        var timeoutWorker = new FakeVoiceWorkerClient { SynthesizeDelay = TimeSpan.FromSeconds(2) };
        var timeoutProvider = new LocalTextToSpeechWorkerProvider(
            "tts",
            timeoutWorker,
            new FakeWorkerSupervisor(LocalWorkerStatus.Running),
            TimeSpan.FromMilliseconds(10));
        var timeout = await timeoutProvider.SynthesizeAsync(new TextToSpeechRequest("hello"));

        var failingWorker = new FakeVoiceWorkerClient
        {
            ThrowOnSynthesize = new InvalidOperationException("raw secret")
        };
        var failingProvider = new LocalTextToSpeechWorkerProvider(
            "tts",
            failingWorker,
            new FakeWorkerSupervisor(LocalWorkerStatus.Running));
        var failure = await failingProvider.SynthesizeAsync(new TextToSpeechRequest("hello"));

        var cancelWorker = new FakeVoiceWorkerClient { SynthesizeDelay = TimeSpan.FromSeconds(2) };
        var cancelProvider = new LocalTextToSpeechWorkerProvider(
            "tts",
            cancelWorker,
            new FakeWorkerSupervisor(LocalWorkerStatus.Running));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await cancelProvider.SynthesizeAsync(
            new TextToSpeechRequest("hello"),
            cancellation.Token);

        Assert.Equal("operation_timeout", timeout.SafeErrorCode);
        Assert.Equal("worker_failure", failure.SafeErrorCode);
        Assert.Equal("operation_cancelled", cancelled.SafeErrorCode);
        Assert.True(cancelWorker.CancelCalled);
    }

    [Fact]
    public async Task AudioCaptureFake_StartStopCancelAndMaxDuration()
    {
        var service = new FakeAudioCaptureService();
        var session = await service.StartCaptureAsync(new AudioCaptureOptions(TimeSpan.FromSeconds(1)));
        var stopped = await session.StopAsync();
        var cancelledSession = await service.StartCaptureAsync(new AudioCaptureOptions(TimeSpan.FromSeconds(1)));
        var cancelled = await cancelledSession.CancelAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await service.StartCaptureAsync(new AudioCaptureOptions(TimeSpan.Zero)));
        Assert.Equal(AudioCaptureStatus.Completed, stopped.Status);
        Assert.Equal(AudioCaptureStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task AudioPlaybackFake_PlayStopCompletionAndFailure()
    {
        var service = new FakeAudioPlaybackService();
        var session = await service.PlayAsync(
            new MemoryStream([1, 2, 3]),
            new SpeechAudioFormat("pcm16", 16000, 1),
            new AudioPlaybackOptions());
        var completed = await session.WaitForCompletionAsync();

        var stoppedSession = await service.PlayAsync(
            new MemoryStream([1]),
            new SpeechAudioFormat("pcm16", 16000, 1),
            new AudioPlaybackOptions());
        var stopped = await stoppedSession.StopAsync();

        service.NextStatus = AudioPlaybackStatus.Failed;
        var failedSession = await service.PlayAsync(
            new MemoryStream([1]),
            new SpeechAudioFormat("pcm16", 16000, 1),
            new AudioPlaybackOptions());
        var failed = await failedSession.WaitForCompletionAsync();

        Assert.Equal(AudioPlaybackStatus.Completed, completed.Status);
        Assert.Equal(AudioPlaybackStatus.Stopped, stopped.Status);
        Assert.Equal("playback_failed", failed.SafeErrorCode);
    }

    [Fact]
    public async Task PushToTalk_StartStopSuccessAndEmitsTaskEvents()
    {
        var store = new MemoryTaskEventStore();
        var service = new PushToTalkService(
            new FakeAudioCaptureService(),
            new FakeSpeechToTextProvider("hello voice"),
            new TaskRuntime(store, new TaskEventBus()));

        var start = await service.StartListeningAsync(
            new PushToTalkStartRequest(new AudioCaptureOptions(TimeSpan.FromSeconds(5))));
        var result = await service.StopAndTranscribeAsync("en");
        var record = await store.GetTaskRunAsync(start.TaskId);

        Assert.True(start.IsSuccess);
        Assert.True(result.IsSuccess);
        Assert.Equal("hello voice", result.Transcript);
        Assert.Contains(record!.Events, item => item.Kind == TaskEventKind.VoiceCaptureStarted);
        Assert.Contains(record.Events, item => item.Kind == TaskEventKind.TranscriptionCompleted);
    }

    [Fact]
    public async Task PushToTalk_RejectsInvalidSessionStatesAndSkipsSttAfterCaptureFailure()
    {
        var stt = new FakeSpeechToTextProvider("unused");
        var service = new PushToTalkService(
            new FakeAudioCaptureService { StopResult = new AudioCaptureResult(AudioCaptureStatus.Failed, SafeErrorCode: "capture_failed") },
            stt,
            new TaskRuntime(new MemoryTaskEventStore(), new TaskEventBus()));

        var stopWithoutSession = await service.StopAndTranscribeAsync();
        var first = await service.StartListeningAsync(
            new PushToTalkStartRequest(new AudioCaptureOptions(TimeSpan.FromSeconds(5))));
        var second = await service.StartListeningAsync(
            new PushToTalkStartRequest(new AudioCaptureOptions(TimeSpan.FromSeconds(5))));
        var failed = await service.StopAndTranscribeAsync();

        Assert.Equal("no_active_voice_session", stopWithoutSession.SafeErrorCode);
        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal("voice_session_active", second.SafeErrorCode);
        Assert.False(failed.IsSuccess);
        Assert.False(stt.WasCalled);
    }

    [Fact]
    public async Task PushToTalk_CancelDuringCaptureAndTranscription()
    {
        var captureService = new FakeAudioCaptureService();
        var service = new PushToTalkService(
            captureService,
            new FakeSpeechToTextProvider("hello"),
            new TaskRuntime(new MemoryTaskEventStore(), new TaskEventBus()));

        await service.StartListeningAsync(
            new PushToTalkStartRequest(new AudioCaptureOptions(TimeSpan.FromSeconds(5))));
        await service.CancelAsync();

        var transcriptionService = new PushToTalkService(
            new FakeAudioCaptureService(),
            new FakeSpeechToTextProvider("hello") { ThrowCancellation = true },
            new TaskRuntime(new MemoryTaskEventStore(), new TaskEventBus()));
        await transcriptionService.StartListeningAsync(
            new PushToTalkStartRequest(new AudioCaptureOptions(TimeSpan.FromSeconds(5))));
        var result = await transcriptionService.StopAndTranscribeAsync();

        Assert.True(captureService.LastSession?.Cancelled);
        Assert.Equal("operation_cancelled", result.SafeErrorCode);
    }

    [Fact]
    public async Task SpeakResponse_SynthesizesThenPlaysAndEmitsTaskEvents()
    {
        var store = new MemoryTaskEventStore();
        var runtime = new TaskRuntime(store, new TaskEventBus());
        var service = new SpeakResponseService(
            new FakeTextToSpeechProvider(),
            new FakeAudioPlaybackService(),
            runtime);

        var result = await service.SpeakTextAsync(new SpeakResponseRequest("hello"));
        var record = await store.GetTaskRunAsync(result.TaskId);

        Assert.True(result.IsSuccess);
        Assert.Contains(record!.Events, item => item.Kind == TaskEventKind.SpeechSynthesisCompleted);
        Assert.Contains(record.Events, item => item.Kind == TaskEventKind.SpeechPlaybackCompleted);
    }

    [Fact]
    public async Task SpeakResponse_FailuresAndCancellationAreSafe()
    {
        var playback = new FakeAudioPlaybackService();
        var synthesisFailure = new SpeakResponseService(
            new FakeTextToSpeechProvider { Result = new TextToSpeechResult(false, new SpeechAudioFormat("pcm16", 16000, 1), TimeSpan.Zero, SafeErrorCode: "synthesis_failed") },
            playback,
            new TaskRuntime(new MemoryTaskEventStore(), new TaskEventBus()));
        var synthesisResult = await synthesisFailure.SpeakTextAsync(new SpeakResponseRequest("hello"));

        var playbackFailure = new SpeakResponseService(
            new FakeTextToSpeechProvider(),
            new FakeAudioPlaybackService { NextStatus = AudioPlaybackStatus.Failed },
            new TaskRuntime(new MemoryTaskEventStore(), new TaskEventBus()));
        var playbackResult = await playbackFailure.SpeakTextAsync(new SpeakResponseRequest("hello"));

        var cancellation = new SpeakResponseService(
            new FakeTextToSpeechProvider { ThrowCancellation = true },
            new FakeAudioPlaybackService(),
            new TaskRuntime(new MemoryTaskEventStore(), new TaskEventBus()));
        var cancelResult = await cancellation.SpeakTextAsync(new SpeakResponseRequest("hello"));

        Assert.Equal("synthesis_failed", synthesisResult.SafeErrorCode);
        Assert.False(playback.WasCalled);
        Assert.Equal("playback_failed", playbackResult.SafeErrorCode);
        Assert.Equal("operation_cancelled", cancelResult.SafeErrorCode);
    }

    [Fact]
    public async Task VoiceSettings_DefaultsAndRoundTripAreSafe()
    {
        var settings = ApplicationSettings.CreateDefault();

        Assert.False(settings.Voice.VoiceInputEnabled);
        Assert.False(settings.Voice.VoiceOutputEnabled);
        Assert.True(settings.Voice.LocalOnly);
        Assert.True(settings.Voice.DeleteTemporaryAudioByDefault);
        Assert.InRange(settings.Voice.MaxRecordingDurationSeconds, 1, 300);

        var path = Path.Combine(
            Path.GetTempPath(),
            "PersonalAI.Tests",
            Guid.NewGuid().ToString("N"),
            "settings.json");
        var service = new JsonApplicationSettingsService(path);
        await service.InitializeAsync();
        await service.SaveAsync(service.Current with
        {
            Voice = service.Current.Voice with
            {
                VoiceInputEnabled = true,
                SpeechToTextProviderId = "local-worker-stt",
                LanguageHint = " en ",
                SpeakingRate = 9.0
            }
        });

        var reloaded = new JsonApplicationSettingsService(path);
        await reloaded.InitializeAsync();

        Assert.True(reloaded.Current.Voice.VoiceInputEnabled);
        Assert.Equal("en", reloaded.Current.Voice.LanguageHint);
        Assert.Equal(2.0, reloaded.Current.Voice.SpeakingRate);
    }

    private sealed class FakeVoiceWorkerClient : IVoiceWorkerClient
    {
        public VoiceWorkerTranscribeRequest? LastTranscribeRequest { get; private set; }
        public VoiceWorkerSynthesizeRequest? LastSynthesizeRequest { get; private set; }
        public TimeSpan? TranscribeDelay { get; init; }
        public TimeSpan? SynthesizeDelay { get; init; }
        public Exception? ThrowOnTranscribe { get; init; }
        public Exception? ThrowOnSynthesize { get; init; }
        public bool CancelCalled { get; private set; }
        public VoiceWorkerTranscribeResult TranscribeResult { get; init; } =
            new(SpeechRecognitionStatus.Completed, "ok", []);
        public VoiceWorkerSynthesizeResult SynthesizeResult { get; init; } =
            new(true, new SpeechAudioFormat("pcm16", 16000, 1), TimeSpan.FromMilliseconds(10), [1, 2, 3]);

        public ValueTask<VoiceWorkerHealthCheckResult> CheckHealthAsync(
            string workerId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new VoiceWorkerHealthCheckResult(
                LocalWorkerStatus.Running,
                VoiceWorkerProtocol.CurrentVersion,
                [VoiceWorkerCapability.SpeechToText, VoiceWorkerCapability.TextToSpeech]));

        public ValueTask<IReadOnlyList<VoiceWorkerLanguage>> ListLanguagesAsync(
            string workerId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<VoiceWorkerLanguage>>(
                [new VoiceWorkerLanguage("en", "English")]);

        public async ValueTask<VoiceWorkerTranscribeResult> TranscribeAsync(
            string workerId,
            VoiceWorkerTranscribeRequest request,
            CancellationToken cancellationToken = default)
        {
            LastTranscribeRequest = request;
            if (TranscribeDelay is not null)
            {
                await Task.Delay(TranscribeDelay.Value, cancellationToken);
            }

            if (ThrowOnTranscribe is not null)
            {
                throw ThrowOnTranscribe;
            }

            return TranscribeResult;
        }

        public ValueTask<IReadOnlyList<VoiceInfo>> ListVoicesAsync(
            string workerId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<VoiceInfo>>(
                [new VoiceInfo("local", "Local Voice")]);

        public async ValueTask<VoiceWorkerSynthesizeResult> SynthesizeAsync(
            string workerId,
            VoiceWorkerSynthesizeRequest request,
            CancellationToken cancellationToken = default)
        {
            LastSynthesizeRequest = request;
            if (SynthesizeDelay is not null)
            {
                await Task.Delay(SynthesizeDelay.Value, cancellationToken);
            }

            if (ThrowOnSynthesize is not null)
            {
                throw ThrowOnSynthesize;
            }

            return SynthesizeResult;
        }

        public ValueTask CancelAsync(
            string workerId,
            VoiceWorkerCancelRequest request,
            CancellationToken cancellationToken = default)
        {
            CancelCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeWorkerSupervisor(
        LocalWorkerStatus status,
        string? safeErrorCode = null) : ILocalWorkerSupervisor
    {
        public ValueTask<LocalWorkerStartResult> StartAsync(
            LocalWorkerDefinition definition,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new LocalWorkerStartResult(definition.Id, status, safeErrorCode));

        public ValueTask<LocalWorkerHealth> GetHealthAsync(
            string workerId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new LocalWorkerHealth(workerId, status, safeErrorCode));

        public ValueTask StopAsync(
            string workerId,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class FakeAudioCaptureService : IAudioCaptureService
    {
        public AudioCaptureResult StopResult { get; init; } = new(
            AudioCaptureStatus.Completed,
            Path.Combine(Path.GetTempPath(), "voice.wav"),
            new SpeechAudioFormat("pcm16", 16000, 1),
            TimeSpan.FromMilliseconds(500));

        public FakeCaptureSession? LastSession { get; private set; }

        public ValueTask<IAudioCaptureSession> StartCaptureAsync(
            AudioCaptureOptions options,
            CancellationToken cancellationToken = default)
        {
            if (options.MaxDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }

            LastSession = new FakeCaptureSession(options, StopResult);
            return ValueTask.FromResult<IAudioCaptureSession>(LastSession);
        }
    }

    private sealed class FakeCaptureSession(
        AudioCaptureOptions options,
        AudioCaptureResult stopResult) : IAudioCaptureSession
    {
        public AudioCaptureOptions Options { get; } = options;
        public bool Cancelled { get; private set; }
        public bool Disposed { get; private set; }

        public ValueTask<AudioCaptureResult> StopAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(stopResult);

        public ValueTask<AudioCaptureResult> CancelAsync(
            CancellationToken cancellationToken = default)
        {
            Cancelled = true;
            return ValueTask.FromResult(new AudioCaptureResult(AudioCaptureStatus.Cancelled));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAudioPlaybackService : IAudioPlaybackService
    {
        public bool WasCalled { get; private set; }
        public AudioPlaybackStatus NextStatus { get; set; } = AudioPlaybackStatus.Completed;

        public ValueTask<IAudioPlaybackSession> PlayAsync(
            Stream audio,
            SpeechAudioFormat format,
            AudioPlaybackOptions options,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return ValueTask.FromResult<IAudioPlaybackSession>(
                new FakePlaybackSession(NextStatus));
        }
    }

    private sealed class FakePlaybackSession(AudioPlaybackStatus status)
        : IAudioPlaybackSession
    {
        public ValueTask<AudioPlaybackResult> StopAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AudioPlaybackResult(AudioPlaybackStatus.Stopped));

        public ValueTask<AudioPlaybackResult> WaitForCompletionAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AudioPlaybackResult(
                status,
                TimeSpan.FromMilliseconds(10),
                status == AudioPlaybackStatus.Failed ? "playback_failed" : null));
    }

    private sealed class FakeSpeechToTextProvider(string transcript)
        : ISpeechToTextProvider
    {
        public bool WasCalled { get; private set; }
        public bool ThrowCancellation { get; init; }
        public string ProviderId => "fake-stt";
        public bool IsLocalOnly => true;
        public bool SupportsStreaming => false;

        public ValueTask<SpeechRecognitionResult> RecognizeAsync(
            SpeechRecognitionRequest request,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            if (ThrowCancellation)
            {
                throw new OperationCanceledException();
            }

            return ValueTask.FromResult(new SpeechRecognitionResult(
                SpeechRecognitionStatus.Completed,
                transcript,
                []));
        }
    }

    private sealed class FakeTextToSpeechProvider : ITextToSpeechProvider
    {
        public bool ThrowCancellation { get; init; }
        public TextToSpeechResult Result { get; init; } = new(
            true,
            new SpeechAudioFormat("pcm16", 16000, 1),
            TimeSpan.FromMilliseconds(10),
            new MemoryStream([1, 2, 3]));

        public string ProviderId => "fake-tts";
        public bool IsLocalOnly => true;

        public ValueTask<IReadOnlyList<VoiceInfo>> ListVoicesAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<VoiceInfo>>(
                [new VoiceInfo("local", "Local Voice")]);

        public ValueTask<TextToSpeechResult> SynthesizeAsync(
            TextToSpeechRequest request,
            CancellationToken cancellationToken = default)
        {
            if (ThrowCancellation)
            {
                throw new OperationCanceledException();
            }

            return ValueTask.FromResult(Result);
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class MemoryTaskEventStore : ITaskEventStore
    {
        private readonly Dictionary<TaskId, TaskRun> _runs = [];
        private readonly Dictionary<TaskId, List<TaskEvent>> _events = [];
        private readonly Dictionary<TaskId, List<TaskArtifact>> _artifacts = [];

        public ValueTask CreateTaskRunAsync(
            TaskRun taskRun,
            CancellationToken cancellationToken = default)
        {
            _runs[taskRun.Id] = taskRun;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateTaskRunStatusAsync(
            TaskId taskId,
            TaskRunStatus status,
            DateTimeOffset updatedAtUtc,
            string? safeErrorCode = null,
            CancellationToken cancellationToken = default)
        {
            _runs[taskId] = _runs[taskId] with
            {
                Status = status,
                UpdatedAtUtc = updatedAtUtc,
                SafeErrorCode = safeErrorCode
            };
            return ValueTask.CompletedTask;
        }

        public ValueTask AppendAsync(
            TaskEvent taskEvent,
            CancellationToken cancellationToken = default)
        {
            if (!_events.TryGetValue(taskEvent.TaskId, out var events))
            {
                events = [];
                _events[taskEvent.TaskId] = events;
            }

            events.Add(taskEvent);
            return ValueTask.CompletedTask;
        }

        public ValueTask AppendArtifactAsync(
            TaskId taskId,
            TaskArtifact artifact,
            CancellationToken cancellationToken = default)
        {
            if (!_artifacts.TryGetValue(taskId, out var artifacts))
            {
                artifacts = [];
                _artifacts[taskId] = artifacts;
            }

            artifacts.Add(artifact);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<TaskRun>> ListRecentTaskRunsAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskRun>>(_runs.Values.Take(limit).ToArray());

        public ValueTask<IReadOnlyList<TaskRun>> ListTaskRunsByStatusAsync(
            TaskRunStatus status,
            int limit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskRun>>(
                _runs.Values.Where(run => run.Status == status).Take(limit).ToArray());

        public ValueTask<TaskRun?> GetLatestTaskRunForConversationAsync(
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                _runs.Values
                    .Where(run => run.ConversationId == conversationId)
                    .OrderByDescending(run => run.UpdatedAtUtc)
                    .FirstOrDefault());

        public ValueTask<TaskRunRecord?> GetTaskRunAsync(
            TaskId taskId,
            CancellationToken cancellationToken = default)
        {
            if (!_runs.TryGetValue(taskId, out var run))
            {
                return ValueTask.FromResult<TaskRunRecord?>(null);
            }

            return ValueTask.FromResult<TaskRunRecord?>(
                new TaskRunRecord(
                    run,
                    _events.GetValueOrDefault(taskId) ?? [],
                    _artifacts.GetValueOrDefault(taskId) ?? []));
        }

        public ValueTask MarkCancelledAsync(
            TaskId taskId,
            TaskCancellationReason reason,
            DateTimeOffset cancelledAtUtc,
            CancellationToken cancellationToken = default) =>
            UpdateTaskRunStatusAsync(
                taskId,
                TaskRunStatus.Cancelled,
                cancelledAtUtc,
                reason.ToString().ToLowerInvariant(),
                cancellationToken);

        public ValueTask MarkFailedAsync(
            TaskId taskId,
            string safeErrorCode,
            DateTimeOffset failedAtUtc,
            CancellationToken cancellationToken = default) =>
            UpdateTaskRunStatusAsync(
                taskId,
                TaskRunStatus.Failed,
                failedAtUtc,
                safeErrorCode,
                cancellationToken);

        public async IAsyncEnumerable<TaskEvent> ReadAsync(
            TaskId taskId,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            foreach (var taskEvent in _events.GetValueOrDefault(taskId) ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return taskEvent;
                await Task.Yield();
            }
        }
    }
}
