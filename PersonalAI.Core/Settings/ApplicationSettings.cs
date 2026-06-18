namespace PersonalAI.Core.Settings;

using PersonalAI.Core.Chat;
using PersonalAI.Core.Voice;

public sealed record ApplicationSettings(
    int SchemaVersion,
    GeneralSettings General,
    AppearanceSettings Appearance,
    ModelSettings Models,
    HotkeySettings Hotkey,
    WindowSettings Window,
    ContextSettings Context,
    PrivacySettings Privacy,
    VisionSettings Vision,
    VoiceSettings Voice)
{
    public const int CurrentSchemaVersion = 3;

    public static ApplicationSettings CreateDefault()
    {
        return new ApplicationSettings(
            CurrentSchemaVersion,
            GeneralSettings.Default,
            AppearanceSettings.Default,
            ModelSettings.Default,
            HotkeySettings.Default,
            WindowSettings.Default,
            ContextSettings.Default,
            PrivacySettings.Default,
            VisionSettings.Default,
            VoiceSettings.CreateDefault());
    }
}

public sealed record GeneralSettings(
    LaunchDestination LaunchDestination,
    bool PreserveComposerDraftBetweenHideShow,
    bool ConfirmBeforeClearingAllContext)
{
    public static GeneralSettings Default { get; } = new(
        LaunchDestination.LastConversation,
        PreserveComposerDraftBetweenHideShow: true,
        ConfirmBeforeClearingAllContext: true);
}

public sealed record AppearanceSettings(
    ThemePreference Theme,
    bool CompactSidebar,
    bool ShowMessageMetadata)
{
    public static AppearanceSettings Default { get; } = new(
        ThemePreference.System,
        CompactSidebar: false,
        ShowMessageMetadata: true);
}

public sealed record ModelSettings(
    IReadOnlyList<ModelRoutingAssignment> Assignments)
{
    public static ModelSettings Default { get; } = new(
        ModelRoutingSettings.CreateDefaultAssignments());
}

public static class ModelRoutingSettings
{
    public const string DefaultModel = "gemma4";

    public static IReadOnlyList<ModelRoutingAssignment> CreateDefaultAssignments()
    {
        return Enum.GetValues<ModelRoutingCategory>()
            .Select(category => new ModelRoutingAssignment(category, DefaultModel))
            .ToArray();
    }
}

public sealed record HotkeySettings(
    bool Control,
    bool Alt,
    bool Shift,
    bool Windows,
    string Key)
{
    public static HotkeySettings Default { get; } = new(
        Control: true,
        Alt: true,
        Shift: false,
        Windows: false,
        Key: "Space");
}

public sealed record WindowSettings(
    CloseBehavior CloseBehavior,
    bool StartMinimizedToTray,
    bool RememberWindowPosition,
    bool LaunchAtSignIn)
{
    public static WindowSettings Default { get; } = new(
        CloseBehavior.HideToTray,
        StartMinimizedToTray: false,
        RememberWindowPosition: true,
        LaunchAtSignIn: false);
}

public sealed record ContextSettings(
    int MaxTotalTextContextCharacters,
    int MaxIndividualClipboardCharacters,
    int MaxAttachedContextItems,
    int ScreenshotMaxPayloadBytes,
    int ScreenshotThumbnailMaxEdge,
    bool ClearAttachmentsAfterSuccessfulSend)
{
    public static ContextSettings Default { get; } = new(
        MaxTotalTextContextCharacters: 24_000,
        MaxIndividualClipboardCharacters: 12_000,
        MaxAttachedContextItems: 8,
        ScreenshotMaxPayloadBytes: 4 * 1024 * 1024,
        ScreenshotThumbnailMaxEdge: 240,
        ClearAttachmentsAfterSuccessfulSend: true);
}

public sealed record PrivacySettings(
    IReadOnlyList<ExcludedApplicationSetting> ExcludedApplications,
    bool IncludeExecutablePathInProviderMetadata,
    bool IncludeWindowTitleInProviderContext)
{
    public static PrivacySettings Default { get; } = new(
        [
            new ExcludedApplicationSetting("CredentialUIBroker", "Windows credentials", true),
            new ExcludedApplicationSetting("LockApp", "Windows lock screen", true),
            new ExcludedApplicationSetting("LogonUI", "Windows sign-in", true),
            new ExcludedApplicationSetting("1Password", "1Password", true),
            new ExcludedApplicationSetting("Bitwarden", "Bitwarden", true),
            new ExcludedApplicationSetting("KeePass", "KeePass", true)
        ],
        IncludeExecutablePathInProviderMetadata: false,
        IncludeWindowTitleInProviderContext: true);
}

public sealed record ExcludedApplicationSetting(
    string ProcessName,
    string? Label,
    bool IsEnabled);

public sealed record VisionSettings(
    IReadOnlyList<string> UserModelPatterns)
{
    public static VisionSettings Default { get; } = new([]);
}

public enum LaunchDestination
{
    LastConversation,
    NewChat
}

public enum ThemePreference
{
    System,
    Light,
    Dark
}

public enum CloseBehavior
{
    HideToTray,
    Exit,
    AskEachTime
}
