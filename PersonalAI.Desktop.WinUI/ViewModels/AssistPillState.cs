namespace PersonalAI.Desktop.WinUI.ViewModels;

public enum AssistPillState
{
    Hidden,
    IdlePill,
    DetectingContext,
    SelectionFallback,
    SpotlightPrompt,
    StreamingResponse,
    Completed,
    Cancelled,
    Failed
}
