using PersonalAI.Infrastructure.ScreenCapture;

namespace PersonalAI.Desktop.Presentation.Services;

public interface IScreenTextCaptureService
{
    Task<ScreenTextCaptureResult> CaptureAsync(
        int maxCharacters,
        int maxPayloadBytes,
        CancellationToken cancellationToken);
}
