using PersonalAI.Core.Workers;
using PersonalAI.Infrastructure.Workers;

namespace PersonalAI.Tests.Workers;

public sealed class LocalWorkerSupervisorTests
{
    [Fact]
    public async Task StartSuccessAndStoppedHealth()
    {
        var process = new FakeWorkerProcess(startResult: true);
        var supervisor = new LocalWorkerSupervisor(new FakeWorkerProcessFactory(process));

        var result = await supervisor.StartAsync(CreateDefinition());
        process.IsRunning = false;
        var health = await supervisor.GetHealthAsync("stt");

        Assert.True(result.IsSuccess);
        Assert.Equal(LocalWorkerStatus.Stopped, health.Status);
    }

    [Fact]
    public async Task StartFailure_ReturnsSafeCodeWithoutCommandLeakage()
    {
        var supervisor = new LocalWorkerSupervisor(
            new FakeWorkerProcessFactory(
                new FakeWorkerProcess(startResult: false)));

        var result = await supervisor.StartAsync(CreateDefinition(
            executablePath: @"C:\secret\worker.exe",
            arguments: ["--token=abc"]));

        Assert.Equal(LocalWorkerStatus.Failed, result.Status);
        Assert.Equal("worker_start_failed", result.SafeErrorCode);
        Assert.DoesNotContain("token", result.SafeErrorCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\secret", result.SafeErrorCode, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledWorker_IsUnavailable()
    {
        var supervisor = new LocalWorkerSupervisor(
            new FakeWorkerProcessFactory(new FakeWorkerProcess(true)));

        var result = await supervisor.StartAsync(CreateDefinition(isEnabled: false));

        Assert.Equal(LocalWorkerStatus.Unavailable, result.Status);
        Assert.Equal("worker_not_enabled", result.SafeErrorCode);
    }

    [Fact]
    public async Task TimeoutAndCancellation_AreHandledSeparately()
    {
        var timeoutSupervisor = new LocalWorkerSupervisor(
            new FakeWorkerProcessFactory(
                new FakeWorkerProcess(
                    startResult: true,
                    delay: TimeSpan.FromSeconds(5))));
        var timeoutResult = await timeoutSupervisor.StartAsync(
            CreateDefinition(timeout: TimeSpan.FromMilliseconds(10)));
        Assert.Equal("worker_start_timeout", timeoutResult.SafeErrorCode);

        var cancelSupervisor = new LocalWorkerSupervisor(
            new FakeWorkerProcessFactory(
                new FakeWorkerProcess(
                    startResult: true,
                    delay: TimeSpan.FromSeconds(5))));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await cancelSupervisor.StartAsync(
                CreateDefinition(timeout: TimeSpan.FromSeconds(5)),
                cancellation.Token));
    }

    private static LocalWorkerDefinition CreateDefinition(
        string executablePath = "worker.exe",
        IReadOnlyList<string>? arguments = null,
        TimeSpan? timeout = null,
        bool isEnabled = true) =>
        new(
            "stt",
            executablePath,
            arguments ?? [],
            timeout ?? TimeSpan.FromSeconds(1),
            isEnabled);

    private sealed class FakeWorkerProcessFactory(IWorkerProcess process)
        : IWorkerProcessFactory
    {
        public IWorkerProcess Create(LocalWorkerDefinition definition) => process;
    }

    private sealed class FakeWorkerProcess(
        bool startResult,
        TimeSpan? delay = null) : IWorkerProcess
    {
        public bool IsRunning { get; set; }

        public async ValueTask<bool> StartAsync(
            LocalWorkerDefinition definition,
            CancellationToken cancellationToken)
        {
            if (delay is not null)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }

            IsRunning = startResult;
            return startResult;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }
}
