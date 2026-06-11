namespace PersonalAI.Core.Settings;

public interface IApplicationSettingsService
{
    ApplicationSettings Current { get; }

    string SettingsPath { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);
}
