namespace PersonalAI.Desktop.WinUI.Services;

public sealed class AssistFocusRestorationService(WindowsGuiFocusController focusController)
{
    public bool TryRestoreFocus(
        ActiveWindowReference? foreground,
        CancellationToken cancellationToken = default)
    {
        if (foreground is null)
        {
            return false;
        }

        var state = focusController.GetState(foreground);
        if (state != GuiFocusRestoreState.Ready)
        {
            return false;
        }

        return focusController.TryRestore(foreground, cancellationToken);
    }

    public bool IsRestorationReady(ActiveWindowReference? foreground)
    {
        if (foreground is null)
        {
            return false;
        }

        return focusController.GetState(foreground) == GuiFocusRestoreState.Ready;
    }
}
