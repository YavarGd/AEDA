using System.Text.Json;
using PersonalAI.Core.Settings;

namespace PersonalAI.Infrastructure.Settings;

public sealed class JsonApplicationSettingsService(string settingsPath)
    : IApplicationSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public JsonApplicationSettingsService()
        : this(ApplicationSettingsPaths.GetDefaultSettingsPath())
    {
    }

    public ApplicationSettings Current { get; private set; } =
        ApplicationSettings.CreateDefault();

    public string SettingsPath { get; } = settingsPath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Current = await LoadAsync(cancellationToken);
        await SaveAsync(Current, cancellationToken);
    }

    public async Task SaveAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            var normalized = ApplicationSettingsValidator.Normalize(settings);

            var directory = Path.GetDirectoryName(SettingsPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = SettingsPath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    normalized,
                    JsonOptions,
                    cancellationToken);
            }

            File.Move(tempPath, SettingsPath, overwrite: true);
            Current = normalized;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        return SaveAsync(ApplicationSettings.CreateDefault(), cancellationToken);
    }

    private async Task<ApplicationSettings> LoadAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return ApplicationSettings.CreateDefault();
            }

            await using var stream = File.OpenRead(SettingsPath);
            var settings = await JsonSerializer.DeserializeAsync<ApplicationSettings>(
                stream,
                JsonOptions,
                cancellationToken);

            return ApplicationSettingsValidator.Normalize(settings);
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is JsonException)
        {
            return ApplicationSettings.CreateDefault();
        }
    }
}
