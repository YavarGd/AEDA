using PersonalAI.Desktop.Presentation.Services;

namespace PersonalAI.Desktop.WinUI.Services;

/// <summary>
/// Retains the rollback shell's settings contract while delegating startup policy to the
/// shared AEDA service. The rollback executable intentionally cannot register itself.
/// </summary>
public sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
    private readonly PersonalAI.Desktop.Presentation.Services.WindowsStartupRegistrationService
        _aedaRegistration = new(() => null);

    public bool IsSupported => _aedaRegistration.IsSupported;

    public bool IsEnabled() => _aedaRegistration.IsEnabled();

    public StartupRegistrationResult SetEnabled(bool enabled) =>
        _aedaRegistration.SetEnabled(enabled);
}
