namespace PersonalAI.Desktop.WinUI.Services;

public interface IStartupRegistrationService
{
    bool IsSupported { get; }

    bool IsEnabled();

    StartupRegistrationResult SetEnabled(bool enabled);
}

public sealed record StartupRegistrationResult(
    bool Succeeded,
    string Message);
