using PersonalAI.Core.Context;
using PersonalAI.Infrastructure.Context;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class ActiveWindowContextService(
    IActiveContextProvider activeContextProvider,
    ForegroundWindowTracker foregroundWindowTracker,
    Func<nint> getOwnWindowHandle)
{
    public async Task<AttachedContextItem?> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var ownHandle = getOwnWindowHandle();
        _ = foregroundWindowTracker.CaptureCurrentExternalWindow(ownHandle);
        var externalWindow = foregroundWindowTracker.GetLastValidExternalWindow();

        if (externalWindow is null)
        {
            return null;
        }

        var context = await activeContextProvider.CaptureAsync(
            new ContextCaptureRequest(
                externalWindow.WindowHandle,
                SelectedText: null,
                CaptureScreenshot: false),
            cancellationToken);

        return context is null
            ? null
            : AttachedContextFactory.FromActiveApplicationContext(context);
    }
}
