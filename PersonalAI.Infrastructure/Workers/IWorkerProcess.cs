using PersonalAI.Core.Workers;

namespace PersonalAI.Infrastructure.Workers;

public interface IWorkerProcess
{
    ValueTask<bool> StartAsync(
        LocalWorkerDefinition definition,
        CancellationToken cancellationToken);

    bool IsRunning { get; }

    ValueTask StopAsync(CancellationToken cancellationToken);
}
