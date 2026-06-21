using PersonalAI.Core.Tasks;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed class TaskSummaryItemViewModel(TaskRun taskRun)
{
    public TaskId Id => taskRun.Id;

    public string Title => string.IsNullOrWhiteSpace(taskRun.Title)
        ? "Untitled task"
        : taskRun.Title;

    public string Status => taskRun.Status.ToString();

    public string Source => string.IsNullOrWhiteSpace(taskRun.Source)
        ? "unknown"
        : taskRun.Source;

    public string UpdatedAt => taskRun.UpdatedAtUtc.ToLocalTime().ToString("g");

    public string SafeSummary => taskRun.SafeErrorCode is null
        ? $"{Status} from {Source}"
        : $"{Status}: {taskRun.SafeErrorCode}";

    public bool IsProblem =>
        taskRun.Status is TaskRunStatus.Failed or TaskRunStatus.Cancelled;
}
