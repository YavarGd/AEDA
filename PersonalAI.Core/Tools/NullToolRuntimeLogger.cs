using PersonalAI.Core.Tasks;

namespace PersonalAI.Core.Tools;

public sealed class NullToolRuntimeLogger : IToolRuntimeLogger
{
    public static NullToolRuntimeLogger Instance { get; } = new();

    private NullToolRuntimeLogger()
    {
    }

    public void ToolException(TaskId taskId, ToolId toolId, Exception exception)
    {
    }
}
