namespace PersonalAI.Desktop.WinUI.ViewModels;

public enum AssistPillState
{
    Hidden,
    IdlePill,
    ContextPrompt,
    SpotlightPrompt,
    StreamingResponse,
    Completed,
    Cancelled,
    Failed
}
