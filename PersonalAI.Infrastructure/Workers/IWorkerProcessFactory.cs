using PersonalAI.Core.Workers;

namespace PersonalAI.Infrastructure.Workers;

public interface IWorkerProcessFactory
{
    IWorkerProcess Create(LocalWorkerDefinition definition);
}
