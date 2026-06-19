namespace PersonalAI.Core.Settings;

using PersonalAI.Core.Chat;
using PersonalAI.Core.Providers;
using PersonalAI.Core.Voice;

public static class ApplicationSettingsValidator
{
    public static ApplicationSettings Normalize(ApplicationSettings? settings)
    {
        var value = settings ?? ApplicationSettings.CreateDefault();
        var defaults = ApplicationSettings.CreateDefault();

        var normalizedVision = NormalizeVision(value.Vision ?? defaults.Vision);

        return value with
        {
            SchemaVersion = ApplicationSettings.CurrentSchemaVersion,
            General = value.General ?? defaults.General,
            Appearance = value.Appearance ?? defaults.Appearance,
            Models = NormalizeModels(value.Models ?? defaults.Models, normalizedVision),
            Hotkey = HotkeySettingsValidator.Normalize(value.Hotkey ?? defaults.Hotkey),
            Window = value.Window ?? defaults.Window,
            Context = NormalizeContext(value.Context ?? defaults.Context),
            Privacy = NormalizePrivacy(value.Privacy ?? defaults.Privacy),
            ProviderRouting = NormalizeProviderRouting(
                value.ProviderRouting ?? defaults.ProviderRouting),
            Vision = normalizedVision,
            Voice = NormalizeVoice(value.Voice ?? defaults.Voice),
            MemoryRag = NormalizeMemoryRag(value.MemoryRag ?? defaults.MemoryRag)
        };
    }

    public static ContextSettings NormalizeContext(ContextSettings settings)
    {
        return settings with
        {
            MaxTotalTextContextCharacters = Clamp(
                settings.MaxTotalTextContextCharacters,
                1_000,
                100_000),
            MaxIndividualClipboardCharacters = Clamp(
                settings.MaxIndividualClipboardCharacters,
                500,
                50_000),
            MaxAttachedContextItems = Clamp(settings.MaxAttachedContextItems, 1, 20),
            ScreenshotMaxPayloadBytes = Clamp(
                settings.ScreenshotMaxPayloadBytes,
                256 * 1024,
                20 * 1024 * 1024),
            ScreenshotThumbnailMaxEdge = Clamp(
                settings.ScreenshotThumbnailMaxEdge,
                64,
                1024)
        };
    }

    public static PrivacySettings NormalizePrivacy(PrivacySettings settings)
    {
        var excluded = settings.ExcludedApplications
            .Select(item => item with
            {
                ProcessName = PrivacyExclusionMatcher.NormalizeProcessName(
                    item.ProcessName)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.ProcessName))
            .GroupBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return settings with { ExcludedApplications = excluded };
    }

    public static VisionSettings NormalizeVision(VisionSettings settings)
    {
        var patterns = settings.UserModelPatterns
            .Select(VisionModelCapabilityRegistry.NormalizePattern)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return settings with { UserModelPatterns = patterns };
    }

    public static VoiceSettings NormalizeVoice(VoiceSettings settings)
    {
        return settings with
        {
            SampleRate = Clamp(settings.SampleRate, 8000, 48000),
            ChannelCount = Clamp(settings.ChannelCount, 1, 2),
            MaxRecordingDurationSeconds = Clamp(
                settings.MaxRecordingDurationSeconds,
                1,
                300),
            SpeakingRate = Math.Clamp(settings.SpeakingRate, 0.5, 2.0),
            SpeechToTextProviderId = NormalizeOptional(settings.SpeechToTextProviderId),
            TextToSpeechProviderId = NormalizeOptional(settings.TextToSpeechProviderId),
            SpeechToTextWorkerId = NormalizeOptional(settings.SpeechToTextWorkerId),
            TextToSpeechWorkerId = NormalizeOptional(settings.TextToSpeechWorkerId),
            LanguageHint = NormalizeOptional(settings.LanguageHint),
            SelectedVoiceId = NormalizeOptional(settings.SelectedVoiceId),
            MicrophoneDeviceId = NormalizeOptional(settings.MicrophoneDeviceId),
            OutputDeviceId = NormalizeOptional(settings.OutputDeviceId)
        };
    }

    public static MemoryRagSettings NormalizeMemoryRag(MemoryRagSettings settings)
    {
        return settings with
        {
            AutomaticMemoryEnabled = settings.AutomaticMemoryEnabled && settings.MemoryEnabled,
            ExplicitMemoryEnabled = settings.ExplicitMemoryEnabled && settings.MemoryEnabled,
            ProjectMemoryEnabled = settings.ProjectMemoryEnabled && settings.MemoryEnabled,
            TaskOutcomeMemoryEnabled = settings.TaskOutcomeMemoryEnabled && settings.MemoryEnabled,
            SensitiveMemoryRequiresApproval = true,
            LocalOnlyMemoryMode = true,
            RetentionDays = Clamp(settings.RetentionDays, 1, 3650),
            MaxMemoryResults = Clamp(settings.MaxMemoryResults, 1, 100),
            WorkspaceIndexingEnabled = settings.WorkspaceIndexingEnabled && settings.RagEnabled,
            EmbeddingEnabled = settings.EmbeddingEnabled && settings.RagEnabled,
            MaxFileSizeForIndexingBytes = Clamp(
                settings.MaxFileSizeForIndexingBytes,
                1024,
                5 * 1024 * 1024),
            MaxChunksPerRun = Clamp(settings.MaxChunksPerRun, 1, 1000),
            MaxEmbeddingInputCharacters = Clamp(
                settings.MaxEmbeddingInputCharacters,
                100,
                100_000),
            EmbeddingBatchSize = Clamp(settings.EmbeddingBatchSize, 1, 128),
            LocalOnlyEmbeddingMode = true,
            SelectedEmbeddingProvider = NormalizeOptional(settings.SelectedEmbeddingProvider),
            SelectedEmbeddingModel = NormalizeOptional(settings.SelectedEmbeddingModel),
            VectorIndexProvider = NormalizeOptional(settings.VectorIndexProvider)
        };
    }

    public static ProviderRoutingSettings NormalizeProviderRouting(
        ProviderRoutingSettings settings)
    {
        var defaults = ProviderRoutingSettings.Default;
        var profiles = (settings.ProviderProfiles ?? defaults.ProviderProfiles)
            .Select(NormalizeProviderProfile)
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Id))
            .GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (profiles.All(profile =>
                !profile.Id.Equals("ollama", StringComparison.OrdinalIgnoreCase)))
        {
            profiles.Insert(0, defaults.ProviderProfiles[0]);
        }

        var selectedChat = NormalizeProviderSelection(
            settings.SelectedChatProvider,
            profiles,
            defaults.SelectedChatProvider);
        var selectedEmbedding = NormalizeProviderSelection(
            settings.SelectedEmbeddingProvider,
            profiles,
            defaults.SelectedEmbeddingProvider);
        var defaultLocal = NormalizeProviderSelection(
            settings.DefaultLocalProvider,
            profiles,
            defaults.DefaultLocalProvider);

        return settings with
        {
            ProviderProfiles = profiles,
            SelectedChatProvider = selectedChat,
            SelectedEmbeddingProvider = selectedEmbedding,
            DefaultLocalProvider = defaultLocal,
            LocalOnlyMode = settings.LocalOnlyMode,
            AllowRemoteChat = !settings.LocalOnlyMode && settings.AllowRemoteChat,
            AllowRemoteEmbeddings = !settings.LocalOnlyMode && settings.AllowRemoteEmbeddings,
            AllowRemoteWithWorkspaceContext = !settings.LocalOnlyMode &&
                settings.AllowRemoteWithWorkspaceContext,
            AllowRemoteWithMemoryContext = !settings.LocalOnlyMode &&
                settings.AllowRemoteWithMemoryContext,
            AllowRemoteWithScreenshots = !settings.LocalOnlyMode &&
                settings.AllowRemoteWithScreenshots,
            AllowRemoteWithClipboardOrAppContext = !settings.LocalOnlyMode &&
                settings.AllowRemoteWithClipboardOrAppContext
        };
    }

    private static ModelSettings NormalizeModels(
        ModelSettings settings,
        VisionSettings visionSettings)
    {
        var assignments = (settings.Assignments ??
                ModelRoutingSettings.CreateDefaultAssignments())
            .Where(assignment => !string.IsNullOrWhiteSpace(assignment.Model))
            .Select(assignment => assignment with { Model = assignment.Model.Trim() })
            .Select(assignment =>
                assignment.Category == ModelRoutingCategory.Vision &&
                !VisionModelCapabilityRegistry.SupportsImages(
                    assignment.Model,
                    visionSettings)
                    ? assignment with { Model = ModelRoutingSettings.DefaultModel }
                    : assignment)
            .GroupBy(assignment => assignment.Category)
            .Select(group => group.First())
            .ToList();

        foreach (var defaultAssignment in
                 ModelRoutingSettings.CreateDefaultAssignments())
        {
            if (assignments.All(assignment =>
                    assignment.Category != defaultAssignment.Category))
            {
                assignments.Add(defaultAssignment);
            }
        }

        return settings with { Assignments = assignments };
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return Math.Min(maximum, Math.Max(minimum, value));
    }

    private static ProviderProfileSetting NormalizeProviderProfile(
        ProviderProfileSetting profile)
    {
        var id = SanitizeToken(profile.Id, "provider");
        var secretReference = NormalizeOptional(profile.SecretReference);
        if (secretReference is not null)
        {
            secretReference = SanitizeToken(secretReference, "secret");
        }

        var endpoint = ProviderEndpointClassifier.Classify(profile.EndpointUrl);
        var endpointUrl = endpoint.BaseUri is null
            ? string.Empty
            : ProviderEndpointClassifier.SafeDisplay(endpoint);
        var isEnabled = profile.IsEnabled && endpoint.IsUsable;

        return profile with
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName)
                ? id
                : profile.DisplayName.Trim(),
            EndpointUrl = endpointUrl,
            IsEnabled = isEnabled,
            ChatModel = NormalizeOptional(profile.ChatModel),
            EmbeddingModel = NormalizeOptional(profile.EmbeddingModel),
            SecretReference = secretReference
        };
    }

    private static string NormalizeProviderSelection(
        string? selected,
        IReadOnlyList<ProviderProfileSetting> profiles,
        string fallback)
    {
        var normalized = NormalizeOptional(selected);
        if (normalized is not null &&
            profiles.Any(profile => profile.Id.Equals(
                normalized,
                StringComparison.OrdinalIgnoreCase)))
        {
            return normalized;
        }

        return fallback;
    }

    private static string SanitizeToken(string value, string fallback)
    {
        var normalized = NormalizeOptional(value) ?? fallback;
        var characters = normalized
            .Where(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.')
            .ToArray();
        return characters.Length == 0 ? fallback : new string(characters);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
