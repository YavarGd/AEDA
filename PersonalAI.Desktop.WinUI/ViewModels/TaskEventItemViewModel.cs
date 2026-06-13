using PersonalAI.Core.Tasks;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed class TaskEventItemViewModel(TaskEvent taskEvent)
{
    public string Timestamp => taskEvent.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");

    public string Kind => taskEvent.Kind.ToString();

    public string Summary => taskEvent.Summary;

    public string Detail =>
        taskEvent.SafeErrorMessage ??
        taskEvent.ProgressLabel ??
        taskEvent.ToolId?.ToString() ??
        string.Empty;
}
