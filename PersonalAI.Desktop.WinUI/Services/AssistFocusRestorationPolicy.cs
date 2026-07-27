using PersonalAI.Core.Context;

namespace PersonalAI.Desktop.WinUI.Services;

public static class AssistFocusRestorationPolicy
{
    public static FocusRestorationRequest CreateRequest(
        ActiveWindowReference? previousForeground,
        FocusRestorationTrigger trigger)
    {
        var shouldRestore = trigger switch
        {
            FocusRestorationTrigger.Dismiss => previousForeground is not null,
            FocusRestorationTrigger.CopyResponse => previousForeground is not null,
            FocusRestorationTrigger.ModuleOpen => false,
            FocusRestorationTrigger.AppOpen => false,
            _ => false
        };

        return new FocusRestorationRequest(previousForeground, trigger, shouldRestore);
    }
}
