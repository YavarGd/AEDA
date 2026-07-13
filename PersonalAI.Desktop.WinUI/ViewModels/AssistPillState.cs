namespace PersonalAI.Desktop.WinUI.ViewModels;

public enum AssistPillState
{
    Hidden,
    IdlePill,
    DetectingContext,
    SpotlightPrompt,
    StreamingResponse,
    Completed,
    Cancelled,
    Failed
}
