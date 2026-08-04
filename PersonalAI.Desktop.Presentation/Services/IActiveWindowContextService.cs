using PersonalAI.Core.Context;
using PersonalAI.Infrastructure.Context;

namespace PersonalAI.Desktop.Presentation.Services;

public interface IActiveWindowContextService
{
    SelectedTextCaptureResult? LastCaptureResult { get; }

    Task<AttachedContextItem?> CaptureAsync(
        AttachedContextItem? explicitContext = null,
        CancellationToken cancellationToken = default);
}
