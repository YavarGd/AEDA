using PersonalAI.Core.Context;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed record FocusRestorationRequest(
    ActiveWindowReference? PreviousForeground,
    FocusRestorationTrigger Trigger,
    bool ShouldRestore);
