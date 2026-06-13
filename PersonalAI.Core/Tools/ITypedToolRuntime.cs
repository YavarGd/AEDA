using PersonalAI.Core.Tasks;

namespace PersonalAI.Core.Tools;

public interface ITypedToolRuntime
{
    ValueTask<ToolResult> InvokeAsync(
        TaskId taskId,
        ToolInvocation invocation,
        CancellationToken cancellationToken = default);
}
