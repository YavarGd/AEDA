namespace PersonalAI.Core.Settings;

using PersonalAI.Core.Chat;
using PersonalAI.Core.Providers;
using PersonalAI.Core.Voice;

public sealed record ApplicationSettings(
    int SchemaVersion,
    GeneralSettings General,
    AppearanceSettings Appearance,
    ModelSettings Models,
    HotkeySettings Hotkey,
    WindowSettings Window,
    AssistPillSettings AssistPill,
    ContextSettings Context,
    PrivacySettings Privacy,
    ProviderRoutingSettings ProviderRouting,
    VisionSettings Vision,
    VoiceSettings Voice,
    MemoryRagSettings MemoryRag)
{
    public const int CurrentSchemaVersion = 6;

    public static ApplicationSettings CreateDefault()
    {
        return new ApplicationSettings(
            CurrentSchemaVersion,
            GeneralSettings.Default,
            AppearanceSettings.Default,
            ModelSettings.Default,
            HotkeySettings.Default,
            WindowSettings.Default,
            AssistPillSettings.Default,
            ContextSettings.Default,
            PrivacySettings.Default,
            ProviderRoutingSettings.Default,
            VisionSettings.Default,
            VoiceSettings.CreateDefault(),
            MemoryRagSettings.Default);
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

public sealed record AssistPillSettings(
    bool Enabled,
    int ResponsePreviewCharacters)
{
    public static AssistPillSettings Default { get; } = new(
        Enabled: true,
        ResponsePreviewCharacters: 1_200);
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

public sealed record ProviderRoutingSettings(
    IReadOnlyList<ProviderProfileSetting> ProviderProfiles,
    string SelectedChatProvider,
    string SelectedEmbeddingProvider,
    string DefaultLocalProvider,
    bool LocalOnlyMode,
    bool AllowRemoteChat,
    bool AllowRemoteEmbeddings,
    bool AllowRemoteWithWorkspaceContext,
    bool AllowRemoteWithMemoryContext,
    bool AllowRemoteWithScreenshots,
    bool AllowRemoteWithClipboardOrAppContext)
{
    public static ProviderRoutingSettings Default { get; } = new(
        [
            new ProviderProfileSetting(
                "ollama",
                ProviderKind.Ollama,
                "Ollama",
                "http://localhost:11434",
                IsEnabled: true,
                ChatModel: ModelRoutingSettings.DefaultModel,
                EmbeddingModel: null,
                SecretReference: null)
        ],
        SelectedChatProvider: "ollama",
        SelectedEmbeddingProvider: "ollama",
        DefaultLocalProvider: "ollama",
        LocalOnlyMode: true,
        AllowRemoteChat: false,
        AllowRemoteEmbeddings: false,
        AllowRemoteWithWorkspaceContext: false,
        AllowRemoteWithMemoryContext: false,
        AllowRemoteWithScreenshots: false,
        AllowRemoteWithClipboardOrAppContext: false);
}

public sealed record ProviderProfileSetting(
    string Id,
    ProviderKind Kind,
    string DisplayName,
    string EndpointUrl,
    bool IsEnabled,
    string? ChatModel,
    string? EmbeddingModel,
    string? SecretReference);

public sealed record MemoryRagSettings(
    bool MemoryEnabled,
    bool ExplicitMemoryEnabled,
    bool AutomaticMemoryEnabled,
    bool ProjectMemoryEnabled,
    bool TaskOutcomeMemoryEnabled,
    bool SensitiveMemoryRequiresApproval,
    bool LocalOnlyMemoryMode,
    int RetentionDays,
    int MaxMemoryResults,
    bool RagEnabled,
    bool WorkspaceIndexingEnabled,
    bool EmbeddingEnabled,
    int MaxFileSizeForIndexingBytes,
    int MaxChunksPerRun,
    int MaxEmbeddingInputCharacters,
    int EmbeddingBatchSize,
    bool LocalOnlyEmbeddingMode,
    string? SelectedEmbeddingProvider,
    string? SelectedEmbeddingModel,
    string? VectorIndexProvider)
{
    public static MemoryRagSettings Default { get; } = new(
        MemoryEnabled: true,
        ExplicitMemoryEnabled: true,
        AutomaticMemoryEnabled: false,
        ProjectMemoryEnabled: true,
        TaskOutcomeMemoryEnabled: true,
        SensitiveMemoryRequiresApproval: true,
        LocalOnlyMemoryMode: true,
        RetentionDays: 365,
        MaxMemoryResults: 20,
        RagEnabled: true,
        WorkspaceIndexingEnabled: false,
        EmbeddingEnabled: false,
        MaxFileSizeForIndexingBytes: 256 * 1024,
        MaxChunksPerRun: 200,
        MaxEmbeddingInputCharacters: 8192,
        EmbeddingBatchSize: 8,
        LocalOnlyEmbeddingMode: true,
        SelectedEmbeddingProvider: null,
        SelectedEmbeddingModel: null,
        VectorIndexProvider: null);
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
