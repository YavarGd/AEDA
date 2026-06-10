using PersonalAI.Core.Context;
using PersonalAI.Infrastructure.Context;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class ScreenshotAttachmentService(
    ScreenshotContextService screenshotContextService)
{
    public Task<AttachedContextItem?> CaptureExternalWindowAsync(
        CancellationToken cancellationToken = default)
    {
        return screenshotContextService.CaptureExternalWindowAsync(cancellationToken);
    }

    public void Release(AttachedContextItem item)
    {
        ScreenshotContextService.Release(item);
    }
}
