using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Settings;
using PersonalAI.Core.Voice;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IApplicationSettingsService _settingsService;
    private readonly IStartupRegistrationService _startupRegistrationService;
    private readonly Func<ApplicationSettings, Task<SettingsApplyResult>> _applyHotkeyAsync;
    private readonly Action<ApplicationSettings> _applyRuntimeSettings;
    private readonly Action _resetWindowPosition;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> _refreshModelsAsync;
    private bool _isLoading;
    private bool _isApplyingHotkey;
    private VoiceSettings _voiceSettings = VoiceSettings.CreateDefault();

    public SettingsViewModel(
        IApplicationSettingsService settingsService,
        IStartupRegistrationService startupRegistrationService,
        Func<ApplicationSettings, Task<SettingsApplyResult>> applyHotkeyAsync,
        Action<ApplicationSettings> applyRuntimeSettings,
        Action resetWindowPosition,
        Func<CancellationToken, Task<IReadOnlyList<string>>> refreshModelsAsync,
        WorkspaceManagementViewModel workspaces)
    {
        _settingsService = settingsService;
        _startupRegistrationService = startupRegistrationService;
        _applyHotkeyAsync = applyHotkeyAsync;
        _applyRuntimeSettings = applyRuntimeSettings;
        _resetWindowPosition = resetWindowPosition;
        _refreshModelsAsync = refreshModelsAsync;
        Workspaces = workspaces;
        Load(settingsService.Current);
    }

    public IReadOnlyList<ThemePreference> ThemeOptions { get; } =
        Enum.GetValues<ThemePreference>();

    public IReadOnlyList<LaunchDestination> LaunchDestinationOptions { get; } =
        Enum.GetValues<LaunchDestination>();

    public IReadOnlyList<CloseBehavior> CloseBehaviorOptions { get; } =
        Enum.GetValues<CloseBehavior>();

    public ObservableCollection<string> InstalledModels { get; } = [];

    public ObservableCollection<string> VisionModels { get; } = [];

    public WorkspaceManagementViewModel Workspaces { get; }

    public string SettingsPath => _settingsService.SettingsPath;

    public int SchemaVersion => _settingsService.Current.SchemaVersion;

    public string SchemaVersionDisplay =>
        $"Settings schema: {_settingsService.Current.SchemaVersion}";

    public bool IsStartupRegistrationSupported =>
        _startupRegistrationService.IsSupported;

    [ObservableProperty]
    private LaunchDestination _launchDestination;

    [ObservableProperty]
    private bool _preserveComposerDraftBetweenHideShow;

    [ObservableProperty]
    private bool _confirmBeforeClearingAllContext;

    [ObservableProperty]
    private ThemePreference _theme;

    [ObservableProperty]
    private bool _compactSidebar;

    [ObservableProperty]
    private bool _showMessageMetadata;

    [ObservableProperty]
    private string _generalModel = string.Empty;

    [ObservableProperty]
    private string _codingModel = string.Empty;

    [ObservableProperty]
    private string _visionModel = string.Empty;

    [ObservableProperty]
    private string _fastModel = string.Empty;

    [ObservableProperty]
    private string _reasoningModel = string.Empty;

    [ObservableProperty]
    private string _modelRefreshStatus = "Models not refreshed yet.";

    [ObservableProperty]
    private bool _hotkeyControl;

    [ObservableProperty]
    private bool _hotkeyAlt;

    [ObservableProperty]
    private bool _hotkeyShift;

    [ObservableProperty]
    private bool _hotkeyWindows;

    [ObservableProperty]
    private string _hotkeyKey = string.Empty;

    [ObservableProperty]
    private CloseBehavior _closeBehavior;

    [ObservableProperty]
    private bool _startMinimizedToTray;

    [ObservableProperty]
    private bool _rememberWindowPosition;

    [ObservableProperty]
    private bool _launchAtSignIn;

    [ObservableProperty]
    private int _maxTotalTextContextCharacters;

    [ObservableProperty]
    private int _maxIndividualClipboardCharacters;

    [ObservableProperty]
    private int _maxAttachedContextItems;

    [ObservableProperty]
    private int _screenshotMaxPayloadBytes;

    [ObservableProperty]
    private int _screenshotThumbnailMaxEdge;

    [ObservableProperty]
    private bool _clearAttachmentsAfterSuccessfulSend;

    [ObservableProperty]
    private string _excludedApplicationsText = string.Empty;

    [ObservableProperty]
    private bool _includeExecutablePathInProviderMetadata;

    [ObservableProperty]
    private bool _includeWindowTitleInProviderContext;

    [ObservableProperty]
    private string _visionPatternsText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Settings ready.";

    public ApplicationSettings BuildSettings()
    {
        var exclusions = ExcludedApplicationsText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => new ExcludedApplicationSetting(line, line, true))
            .ToArray();
        var visionPatterns = VisionPatternsText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        return ApplicationSettingsValidator.Normalize(new ApplicationSettings(
            ApplicationSettings.CurrentSchemaVersion,
            new GeneralSettings(
                LaunchDestination,
                PreserveComposerDraftBetweenHideShow,
                ConfirmBeforeClearingAllContext),
            new AppearanceSettings(Theme, CompactSidebar, ShowMessageMetadata),
            new ModelSettings(
                [
                    new ModelRoutingAssignment(
                        ModelRoutingCategory.General,
                        GeneralModel),
                    new ModelRoutingAssignment(
                        ModelRoutingCategory.Coding,
                        CodingModel),
                    new ModelRoutingAssignment(
                        ModelRoutingCategory.Vision,
                        VisionModel),
                    new ModelRoutingAssignment(
                        ModelRoutingCategory.Fast,
                        FastModel),
                    new ModelRoutingAssignment(
                        ModelRoutingCategory.Reasoning,
                        ReasoningModel)
                ]),
            new HotkeySettings(
                HotkeyControl,
                HotkeyAlt,
                HotkeyShift,
                HotkeyWindows,
                HotkeyKey),
            new WindowSettings(
                CloseBehavior,
                StartMinimizedToTray,
                RememberWindowPosition,
                LaunchAtSignIn),
            new ContextSettings(
                MaxTotalTextContextCharacters,
                MaxIndividualClipboardCharacters,
                MaxAttachedContextItems,
                ScreenshotMaxPayloadBytes,
                ScreenshotThumbnailMaxEdge,
                ClearAttachmentsAfterSuccessfulSend),
            new PrivacySettings(
                exclusions,
                IncludeExecutablePathInProviderMetadata,
                IncludeWindowTitleInProviderContext),
            new VisionSettings(visionPatterns),
            _voiceSettings));
    }

    public async Task SaveSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_isLoading)
        {
            return;
        }

        var settings = BuildSettings();
        await _settingsService.SaveAsync(settings, cancellationToken);
        _applyRuntimeSettings(_settingsService.Current);
        StatusMessage = "Settings saved.";
    }

    [RelayCommand]
    public async Task ApplyHotkeyAsync()
    {
        if (_isApplyingHotkey)
        {
            return;
        }

        _isApplyingHotkey = true;
        var settings = BuildSettings();

        try
        {
            var result = await HotkeyApplyCoordinator.ApplyAsync(
                settings.Hotkey,
                _settingsService.Current.Hotkey,
                async (hotkey, _) =>
                {
                    var candidate = settings with { Hotkey = hotkey };
                    var applyResult = await _applyHotkeyAsync(candidate);
                    StatusMessage = applyResult.Message;
                    return applyResult.Succeeded;
                },
                async (hotkey, cancellationToken) =>
                {
                    var accepted = settings with { Hotkey = hotkey };
                    await _settingsService.SaveAsync(accepted, cancellationToken);
                });

            if (!result.Succeeded)
            {
                RestoreHotkeyDraft();
                StatusMessage = result.Message;
                return;
            }

            Load(_settingsService.Current);
            _applyRuntimeSettings(_settingsService.Current);
            StatusMessage = result.Message;
        }
        finally
        {
            _isApplyingHotkey = false;
        }
    }

    [RelayCommand]
    public async Task ResetHotkeyAsync()
    {
        HotkeyControl = HotkeySettings.Default.Control;
        HotkeyAlt = HotkeySettings.Default.Alt;
        HotkeyShift = HotkeySettings.Default.Shift;
        HotkeyWindows = HotkeySettings.Default.Windows;
        HotkeyKey = HotkeySettings.Default.Key;
        await ApplyHotkeyAsync();
    }

    [RelayCommand]
    public async Task RefreshModelsAsync()
    {
        try
        {
            var models = await _refreshModelsAsync(CancellationToken.None);
            InstalledModels.Clear();
            VisionModels.Clear();

            foreach (var model in models)
            {
                InstalledModels.Add(model);
                if (VisionModelCapabilityRegistry.SupportsImages(
                        model,
                        _settingsService.Current.Vision))
                {
                    VisionModels.Add(model);
                }
            }

            ModelRefreshStatus = models.Count == 0
                ? "Ollama returned no installed models."
                : $"Loaded {models.Count} installed model(s).";

            FillMissingModelAssignments(models);
        }
        catch (Exception exception) when (
            exception is HttpRequestException ||
            exception is InvalidOperationException ||
            exception is TaskCanceledException)
        {
            ModelRefreshStatus =
                $"Ollama models unavailable: {exception.Message}";
        }
    }

    [RelayCommand]
    public async Task ResetModelAssignmentsAsync()
    {
        if (InstalledModels.Count == 0)
        {
            await RefreshModelsAsync();
        }

        var models = InstalledModels.ToArray();

        if (models.Length == 0)
        {
            GeneralModel = ModelRoutingSettings.DefaultModel;
            CodingModel = ModelRoutingSettings.DefaultModel;
            VisionModel = ModelRoutingSettings.DefaultModel;
            FastModel = ModelRoutingSettings.DefaultModel;
            ReasoningModel = ModelRoutingSettings.DefaultModel;
            StatusMessage = "Model assignments reset to built-in defaults.";
            return;
        }

        var general = models[0];
        var vision = models.FirstOrDefault(model =>
            VisionModelCapabilityRegistry.SupportsImages(
                model,
                _settingsService.Current.Vision));

        GeneralModel = general;
        CodingModel = general;
        FastModel = general;
        ReasoningModel = general;
            VisionModel = vision ?? string.Empty;
        await SaveSettingsAsync();
        StatusMessage = "Model assignments reset to detected defaults.";
    }

    [RelayCommand]
    public async Task ToggleStartupAsync()
    {
        var result = _startupRegistrationService.SetEnabled(LaunchAtSignIn);

        if (!result.Succeeded)
        {
            LaunchAtSignIn = _settingsService.Current.Window.LaunchAtSignIn;
            StatusMessage = result.Message;
            return;
        }

        await SaveSettingsAsync();
        StatusMessage = result.Message;
    }

    [RelayCommand]
    public async Task ResetAllAsync()
    {
        await _settingsService.ResetAsync();
        Load(_settingsService.Current);
        _applyRuntimeSettings(_settingsService.Current);
        StatusMessage = "Settings reset.";
    }

    [RelayCommand]
    public void ResetWindowPosition()
    {
        _resetWindowPosition();
        StatusMessage = "Window position reset.";
    }

    [RelayCommand]
    public void OpenSettingsFolder()
    {
        var directory = Path.GetDirectoryName(SettingsPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            StatusMessage = "Settings folder is unavailable.";
            return;
        }

        Directory.CreateDirectory(directory);
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        };

        System.Diagnostics.Process.Start(startInfo);
        StatusMessage = "Settings folder opened.";
    }

    partial void OnLaunchDestinationChanged(LaunchDestination value) =>
        QueueSave();

    partial void OnPreserveComposerDraftBetweenHideShowChanged(bool value) =>
        QueueSave();

    partial void OnConfirmBeforeClearingAllContextChanged(bool value) =>
        QueueSave();

    partial void OnThemeChanged(ThemePreference value) => QueueSave();

    partial void OnCompactSidebarChanged(bool value) => QueueSave();

    partial void OnShowMessageMetadataChanged(bool value) => QueueSave();

    partial void OnGeneralModelChanged(string value) => QueueSaveModels();

    partial void OnCodingModelChanged(string value) => QueueSaveModels();

    partial void OnVisionModelChanged(string value) => QueueSaveModels();

    partial void OnFastModelChanged(string value) => QueueSaveModels();

    partial void OnReasoningModelChanged(string value) => QueueSaveModels();

    partial void OnCloseBehaviorChanged(CloseBehavior value) => QueueSave();

    partial void OnStartMinimizedToTrayChanged(bool value) => QueueSave();

    partial void OnRememberWindowPositionChanged(bool value) => QueueSave();

    partial void OnMaxTotalTextContextCharactersChanged(int value) => QueueSave();

    partial void OnMaxIndividualClipboardCharactersChanged(int value) => QueueSave();

    partial void OnMaxAttachedContextItemsChanged(int value) => QueueSave();

    partial void OnScreenshotMaxPayloadBytesChanged(int value) => QueueSave();

    partial void OnScreenshotThumbnailMaxEdgeChanged(int value) => QueueSave();

    partial void OnClearAttachmentsAfterSuccessfulSendChanged(bool value) =>
        QueueSave();

    partial void OnExcludedApplicationsTextChanged(string value) => QueueSave();

    partial void OnIncludeExecutablePathInProviderMetadataChanged(bool value) =>
        QueueSave();

    partial void OnIncludeWindowTitleInProviderContextChanged(bool value) =>
        QueueSave();

    partial void OnVisionPatternsTextChanged(string value) => QueueSave();

    private void Load(ApplicationSettings settings)
    {
        _isLoading = true;

        try
        {
            LaunchDestination = settings.General.LaunchDestination;
            PreserveComposerDraftBetweenHideShow =
                settings.General.PreserveComposerDraftBetweenHideShow;
            ConfirmBeforeClearingAllContext =
                settings.General.ConfirmBeforeClearingAllContext;
            Theme = settings.Appearance.Theme;
            CompactSidebar = settings.Appearance.CompactSidebar;
            ShowMessageMetadata = settings.Appearance.ShowMessageMetadata;
            GeneralModel = GetAssignment(
                settings.Models,
                ModelRoutingCategory.General);
            CodingModel = GetAssignment(
                settings.Models,
                ModelRoutingCategory.Coding);
            VisionModel = GetAssignment(
                settings.Models,
                ModelRoutingCategory.Vision);
            FastModel = GetAssignment(
                settings.Models,
                ModelRoutingCategory.Fast);
            ReasoningModel = GetAssignment(
                settings.Models,
                ModelRoutingCategory.Reasoning);
            HotkeyControl = settings.Hotkey.Control;
            HotkeyAlt = settings.Hotkey.Alt;
            HotkeyShift = settings.Hotkey.Shift;
            HotkeyWindows = settings.Hotkey.Windows;
            HotkeyKey = settings.Hotkey.Key;
            CloseBehavior = settings.Window.CloseBehavior;
            StartMinimizedToTray = settings.Window.StartMinimizedToTray;
            RememberWindowPosition = settings.Window.RememberWindowPosition;
            LaunchAtSignIn = settings.Window.LaunchAtSignIn;
            MaxTotalTextContextCharacters =
                settings.Context.MaxTotalTextContextCharacters;
            MaxIndividualClipboardCharacters =
                settings.Context.MaxIndividualClipboardCharacters;
            MaxAttachedContextItems = settings.Context.MaxAttachedContextItems;
            ScreenshotMaxPayloadBytes = settings.Context.ScreenshotMaxPayloadBytes;
            ScreenshotThumbnailMaxEdge =
                settings.Context.ScreenshotThumbnailMaxEdge;
            ClearAttachmentsAfterSuccessfulSend =
                settings.Context.ClearAttachmentsAfterSuccessfulSend;
            ExcludedApplicationsText = string.Join(
                Environment.NewLine,
                settings.Privacy.ExcludedApplications.Select(item => item.ProcessName));
            IncludeExecutablePathInProviderMetadata =
                settings.Privacy.IncludeExecutablePathInProviderMetadata;
            IncludeWindowTitleInProviderContext =
                settings.Privacy.IncludeWindowTitleInProviderContext;
            VisionPatternsText = string.Join(
                Environment.NewLine,
                settings.Vision.UserModelPatterns);
            _voiceSettings = ApplicationSettingsValidator.NormalizeVoice(settings.Voice);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void QueueSave()
    {
        if (_isLoading)
        {
            return;
        }

        _ = SaveSettingsAsync();
    }

    private void QueueSaveModels()
    {
        if (_isLoading)
        {
            return;
        }

        if (!ValidateVisionAssignment(out var errorMessage))
        {
            StatusMessage = errorMessage;
            return;
        }

        QueueSave();
    }

    private bool ValidateVisionAssignment(out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(VisionModel) ||
            InstalledModels.Count == 0 ||
            InstalledModels.All(model =>
                !model.Equals(VisionModel, StringComparison.OrdinalIgnoreCase)))
        {
            errorMessage = string.Empty;
            return true;
        }

        if (VisionModelCapabilityRegistry.SupportsImages(
                VisionModel,
                new VisionSettings(
                    VisionPatternsText
                        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => line.Trim())
                        .Where(line => line.Length > 0)
                        .ToArray())))
        {
            errorMessage = string.Empty;
            return true;
        }

        errorMessage =
            $"'{VisionModel}' is not configured as vision-capable; choose a vision model.";
        return false;
    }

    private void FillMissingModelAssignments(IReadOnlyList<string> models)
    {
        if (models.Count == 0)
        {
            return;
        }

        var first = models[0];
        var firstVision = models.FirstOrDefault(model =>
            VisionModelCapabilityRegistry.SupportsImages(
                model,
                _settingsService.Current.Vision));

        GeneralModel = ChooseExistingOrDefault(GeneralModel, models, first);
        CodingModel = ChooseExistingOrDefault(CodingModel, models, GeneralModel);
        FastModel = ChooseExistingOrDefault(FastModel, models, GeneralModel);
        ReasoningModel = ChooseExistingOrDefault(
            ReasoningModel,
            models,
            GeneralModel);
        VisionModel = ChooseVisionOrEmpty(VisionModel, models, firstVision);
    }

    private void RestoreHotkeyDraft()
    {
        _isLoading = true;

        try
        {
            HotkeyControl = _settingsService.Current.Hotkey.Control;
            HotkeyAlt = _settingsService.Current.Hotkey.Alt;
            HotkeyShift = _settingsService.Current.Hotkey.Shift;
            HotkeyWindows = _settingsService.Current.Hotkey.Windows;
            HotkeyKey = _settingsService.Current.Hotkey.Key;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static string GetAssignment(
        ModelSettings settings,
        ModelRoutingCategory category)
    {
        return settings.Assignments.FirstOrDefault(assignment =>
            assignment.Category == category)?.Model ??
            ModelRoutingSettings.DefaultModel;
    }

    private static string ChooseExistingOrDefault(
        string current,
        IReadOnlyList<string> models,
        string fallback)
    {
        return models.FirstOrDefault(model =>
            model.Equals(current, StringComparison.OrdinalIgnoreCase)) ??
            fallback;
    }

    private static string ChooseVisionOrEmpty(
        string current,
        IReadOnlyList<string> models,
        string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(current) &&
            models.Any(model =>
                model.Equals(current, StringComparison.OrdinalIgnoreCase)) &&
            VisionModelCapabilityRegistry.SupportsImages(current, VisionSettings.Default))
        {
            return current;
        }

        return fallback ?? string.Empty;
    }
}
