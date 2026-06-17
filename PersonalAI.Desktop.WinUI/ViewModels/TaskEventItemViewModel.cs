using PersonalAI.Core.Tasks;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed class TaskEventItemViewModel(TaskEvent taskEvent)
{
    private readonly ToolActivityPresentation _presentation =
        ToolPresentationMapper.ForTaskEvent(taskEvent);

    public string Timestamp => taskEvent.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");

    public string Kind => _presentation.State;

    public string Summary => _presentation.Title;

    public string Detail => _presentation.Detail;

    public bool IsProblem => _presentation.IsProblem;
}
