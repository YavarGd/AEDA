namespace PersonalAI.Desktop.Presentation.ViewModels;

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
