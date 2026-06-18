using System.Collections.Concurrent;
using PersonalAI.Core.Workers;

namespace PersonalAI.Infrastructure.Workers;

public sealed class LocalWorkerSupervisor : ILocalWorkerSupervisor
{
    private readonly IWorkerProcessFactory _processFactory;
    private readonly ConcurrentDictionary<string, IWorkerProcess> _processes = [];
    private readonly ConcurrentDictionary<string, LocalWorkerHealth> _health = [];

    public LocalWorkerSupervisor(IWorkerProcessFactory processFactory)
    {
        _processFactory = processFactory ??
            throw new ArgumentNullException(nameof(processFactory));
    }

    public async ValueTask<LocalWorkerStartResult> StartAsync(
        LocalWorkerDefinition definition,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(definition);

        if (!definition.IsEnabled)
        {
            return SetHealth(
                definition.Id,
                LocalWorkerStatus.Unavailable,
                "worker_not_enabled");
        }

        if (string.IsNullOrWhiteSpace(definition.ExecutablePath))
        {
            return SetHealth(
                definition.Id,
                LocalWorkerStatus.Failed,
                "worker_path_required");
        }

        using var timeout = new CancellationTokenSource(definition.StartTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        var process = _processFactory.Create(definition);
        _health[definition.Id] = new LocalWorkerHealth(
            definition.Id,
            LocalWorkerStatus.Starting);

        try
        {
            if (!await process.StartAsync(definition, linked.Token))
            {
                return SetHealth(
                    definition.Id,
                    LocalWorkerStatus.Failed,
                    "worker_start_failed");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return SetHealth(
                definition.Id,
                LocalWorkerStatus.Failed,
                "worker_start_timeout");
        }
        catch (Exception)
        {
            return SetHealth(
                definition.Id,
                LocalWorkerStatus.Failed,
                "worker_start_failed");
        }

        _processes[definition.Id] = process;
        return SetHealth(definition.Id, LocalWorkerStatus.Running);
    }

    public ValueTask<LocalWorkerHealth> GetHealthAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_processes.TryGetValue(workerId, out var process) && !process.IsRunning)
        {
            _health[workerId] = new LocalWorkerHealth(
                workerId,
                LocalWorkerStatus.Stopped);
        }

        return ValueTask.FromResult(
            _health.TryGetValue(workerId, out var health)
                ? health
                : new LocalWorkerHealth(workerId, LocalWorkerStatus.Unavailable));
    }

    public async ValueTask StopAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_processes.TryRemove(workerId, out var process))
        {
            await process.StopAsync(cancellationToken);
        }

        _health[workerId] = new LocalWorkerHealth(
            workerId,
            LocalWorkerStatus.Stopped);
    }

    private LocalWorkerStartResult SetHealth(
        string workerId,
        LocalWorkerStatus status,
        string? safeErrorCode = null)
    {
        _health[workerId] = new LocalWorkerHealth(workerId, status, safeErrorCode);
        return new LocalWorkerStartResult(workerId, status, safeErrorCode);
    }
}
