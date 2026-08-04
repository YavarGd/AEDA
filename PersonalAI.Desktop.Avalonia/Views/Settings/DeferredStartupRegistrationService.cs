using PersonalAI.Desktop.Presentation.Services;

namespace PersonalAI.Desktop.Avalonia.Views.Settings;

internal sealed class DeferredStartupRegistrationService : IStartupRegistrationService
{
    public bool IsSupported => false;

    public bool IsEnabled() => false;

    public StartupRegistrationResult SetEnabled(bool enabled) =>
        new(false, "Startup registration remains managed by the WinUI app during migration.");
}
