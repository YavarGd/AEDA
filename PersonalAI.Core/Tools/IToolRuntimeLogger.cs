using PersonalAI.Core.Tasks;

namespace PersonalAI.Core.Tools;

public interface IToolRuntimeLogger
{
    void ToolException(TaskId taskId, ToolId toolId, Exception exception);
}
