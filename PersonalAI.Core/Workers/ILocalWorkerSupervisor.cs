namespace PersonalAI.Core.Workers;

public interface ILocalWorkerSupervisor
{
    ValueTask<LocalWorkerStartResult> StartAsync(
        LocalWorkerDefinition definition,
        CancellationToken cancellationToken = default);

    ValueTask<LocalWorkerHealth> GetHealthAsync(
        string workerId,
        CancellationToken cancellationToken = default);

    ValueTask StopAsync(
        string workerId,
        CancellationToken cancellationToken = default);
}
