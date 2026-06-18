using PersonalAI.Core.Chat;
using PersonalAI.Core.Settings;
using PersonalAI.Infrastructure.Settings;

namespace PersonalAI.Tests.Settings;

public sealed class ApplicationSettingsTests
{
    [Fact]
    public void DefaultsUseExpectedSafeValues()
    {
        var settings = ApplicationSettings.CreateDefault();

        Assert.Contains(settings.Models.Assignments, assignment =>
            assignment.Category == ModelRoutingCategory.General &&
            assignment.Model == "gemma4");
        Assert.Equal(24_000, settings.Context.MaxTotalTextContextCharacters);
        Assert.Equal(CloseBehavior.HideToTray, settings.Window.CloseBehavior);
        Assert.True(settings.Hotkey.Control);
        Assert.True(settings.Hotkey.Alt);
        Assert.Equal("Space", settings.Hotkey.Key);
        Assert.True(settings.MemoryRag.MemoryEnabled);
        Assert.True(settings.MemoryRag.ExplicitMemoryEnabled);
        Assert.False(settings.MemoryRag.AutomaticMemoryEnabled);
        Assert.False(settings.MemoryRag.WorkspaceIndexingEnabled);
        Assert.True(settings.MemoryRag.LocalOnlyMemoryMode);
        Assert.True(settings.MemoryRag.SensitiveMemoryRequiresApproval);
    }

    [Fact]
    public void ContextValidationClampsUnsafeRanges()
    {
        var normalized = ApplicationSettingsValidator.NormalizeContext(new ContextSettings(
            MaxTotalTextContextCharacters: -1,
            MaxIndividualClipboardCharacters: 10,
            MaxAttachedContextItems: 999,
            ScreenshotMaxPayloadBytes: 1,
            ScreenshotThumbnailMaxEdge: 10_000,
            ClearAttachmentsAfterSuccessfulSend: true));

        Assert.Equal(1_000, normalized.MaxTotalTextContextCharacters);
        Assert.Equal(500, normalized.MaxIndividualClipboardCharacters);
        Assert.Equal(20, normalized.MaxAttachedContextItems);
        Assert.Equal(256 * 1024, normalized.ScreenshotMaxPayloadBytes);
        Assert.Equal(1024, normalized.ScreenshotThumbnailMaxEdge);
    }

    [Theory]
    [InlineData("Bitwarden.exe", "bitwarden")]
    [InlineData("  KeePass  ", "KeePass")]
    public void PrivacyMatcherNormalizesProcessNames(
        string processName,
        string excludedName)
    {
        var exclusions = new[]
        {
            new ExcludedApplicationSetting(excludedName, null, true)
        };

        Assert.True(PrivacyExclusionMatcher.IsExcluded(processName, exclusions));
    }

    [Fact]
    public void PrivacyValidationDeduplicatesExclusions()
    {
        var normalized = ApplicationSettingsValidator.NormalizePrivacy(
            new PrivacySettings(
                [
                    new ExcludedApplicationSetting("Bitwarden.exe", null, true),
                    new ExcludedApplicationSetting("bitwarden", null, true)
                ],
                IncludeExecutablePathInProviderMetadata: false,
                IncludeWindowTitleInProviderContext: true));

        Assert.Single(normalized.ExcludedApplications);
        Assert.Equal("Bitwarden", normalized.ExcludedApplications[0].ProcessName);
    }

    [Fact]
    public async Task RouterUsesExpectedCategoryPrecedence()
    {
        var router = new DeterministicChatModelRouter();
        var assignments = new[]
        {
            new ModelRoutingAssignment(ModelRoutingCategory.General, "general"),
            new ModelRoutingAssignment(ModelRoutingCategory.Coding, "coding"),
            new ModelRoutingAssignment(ModelRoutingCategory.Vision, "gemma4"),
            new ModelRoutingAssignment(ModelRoutingCategory.Fast, "fast"),
            new ModelRoutingAssignment(ModelRoutingCategory.Reasoning, "reasoning")
        };

        var coding = await router.SelectModelAsync(new ModelRoutingRequest(
            "Fix this C# exception",
            [],
            ["general", "coding", "gemma4"],
            assignments));
        var vision = await router.SelectModelAsync(new ModelRoutingRequest(
            "Explain this image",
            [new AttachedContextSignal("Screenshot", HasImage: true)],
            ["general", "coding", "gemma4"],
            assignments));
        var general = await router.SelectModelAsync(new ModelRoutingRequest(
            "Summarize this",
            [],
            ["general", "coding", "gemma4"],
            assignments));

        Assert.Equal("coding", coding.SelectedModel);
        Assert.Equal(ModelRoutingCategory.Coding, coding.Category);
        Assert.Equal("gemma4", vision.SelectedModel);
        Assert.Equal(ModelRoutingCategory.Vision, vision.Category);
        Assert.Equal("general", general.SelectedModel);
    }

    [Theory]
    [InlineData(false, false, false, false, "Space")]
    [InlineData(true, false, false, false, "Shift")]
    [InlineData(true, false, false, false, "")]
    public void HotkeyValidatorRejectsInvalidShortcuts(
        bool control,
        bool alt,
        bool shift,
        bool windows,
        string key)
    {
        var result = HotkeySettingsValidator.Validate(
            new HotkeySettings(control, alt, shift, windows, key));

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void HotkeyValidatorFormatsNormalizedShortcut()
    {
        var hotkey = new HotkeySettings(
            Control: true,
            Alt: true,
            Shift: true,
            Windows: false,
            Key: "space");

        Assert.Equal("Ctrl+Alt+Shift+Space", HotkeySettingsValidator.Format(hotkey));
    }

    [Fact]
    public async Task HotkeyApplyCoordinatorSavesSuccessfulRegistration()
    {
        HotkeySettings? saved = null;
        var result = await HotkeyApplyCoordinator.ApplyAsync(
            new HotkeySettings(true, false, false, false, "k"),
            HotkeySettings.Default,
            (hotkey, _) => Task.FromResult(hotkey.Key == "K"),
            (hotkey, _) =>
            {
                saved = hotkey;
                return Task.CompletedTask;
            });

        Assert.True(result.Succeeded);
        Assert.Equal("K", result.AcceptedHotkey.Key);
        Assert.Equal("K", saved?.Key);
    }

    [Fact]
    public async Task HotkeyApplyCoordinatorRollsBackFailedRegistration()
    {
        var saved = false;
        var result = await HotkeyApplyCoordinator.ApplyAsync(
            new HotkeySettings(true, false, false, false, "k"),
            HotkeySettings.Default,
            (_, _) => Task.FromResult(false),
            (_, _) =>
            {
                saved = true;
                return Task.CompletedTask;
            });

        Assert.False(result.Succeeded);
        Assert.Equal(HotkeySettings.Default, result.AcceptedHotkey);
        Assert.False(saved);
    }

    [Theory]
    [InlineData("gemma4:latest")]
    [InlineData("llama3.2-vision:11b")]
    [InlineData("qwen3-vl:8b")]
    [InlineData("custom-vision")]
    public void VisionRegistrySupportsTaggedAndUserPatterns(string model)
    {
        var settings = new VisionSettings(["custom-vision"]);

        Assert.True(VisionModelCapabilityRegistry.SupportsImages(model, settings));
    }

    [Fact]
    public void VisionRegistryAvoidsLooseSubstringMatches()
    {
        Assert.False(VisionModelCapabilityRegistry.SupportsImages(
            "notgemma4text",
            VisionSettings.Default));
        Assert.False(VisionModelCapabilityRegistry.SupportsImages(
            "qwen3:8b",
            VisionSettings.Default));
    }

    [Fact]
    public void MemoryRagValidationClampsUnsafeRangesAndSafeFlags()
    {
        var normalized = ApplicationSettingsValidator.NormalizeMemoryRag(
            new MemoryRagSettings(
                MemoryEnabled: true,
                ExplicitMemoryEnabled: true,
                AutomaticMemoryEnabled: true,
                ProjectMemoryEnabled: true,
                TaskOutcomeMemoryEnabled: true,
                SensitiveMemoryRequiresApproval: false,
                LocalOnlyMemoryMode: false,
                RetentionDays: 99_999,
                MaxMemoryResults: 999,
                RagEnabled: false,
                WorkspaceIndexingEnabled: true,
                EmbeddingEnabled: true,
                MaxFileSizeForIndexingBytes: 10,
                MaxChunksPerRun: 10_000,
                MaxEmbeddingInputCharacters: 10,
                EmbeddingBatchSize: 999,
                LocalOnlyEmbeddingMode: false,
                SelectedEmbeddingProvider: " fake ",
                SelectedEmbeddingModel: " nomic ",
                VectorIndexProvider: " memory "));

        Assert.True(normalized.SensitiveMemoryRequiresApproval);
        Assert.True(normalized.LocalOnlyMemoryMode);
        Assert.Equal(3650, normalized.RetentionDays);
        Assert.Equal(100, normalized.MaxMemoryResults);
        Assert.False(normalized.WorkspaceIndexingEnabled);
        Assert.False(normalized.EmbeddingEnabled);
        Assert.Equal(1024, normalized.MaxFileSizeForIndexingBytes);
        Assert.Equal(1000, normalized.MaxChunksPerRun);
        Assert.Equal(100, normalized.MaxEmbeddingInputCharacters);
        Assert.Equal(128, normalized.EmbeddingBatchSize);
        Assert.True(normalized.LocalOnlyEmbeddingMode);
        Assert.Equal("fake", normalized.SelectedEmbeddingProvider);
        Assert.Equal("nomic", normalized.SelectedEmbeddingModel);
        Assert.Equal("memory", normalized.VectorIndexProvider);
    }

    [Fact]
    public async Task RouterHonorsExplicitInstalledOverrideAndRemovesDirective()
    {
        var router = new DeterministicChatModelRouter();
        var decision = await router.SelectModelAsync(new ModelRoutingRequest(
            "Explain this.",
            [],
            ["gemma4", "qwen:latest"],
            ModelRoutingSettings.CreateDefaultAssignments())
        {
            ExplicitModelOverride = "qwen:latest"
        });

        Assert.True(decision.ExplicitOverrideHonored);
        Assert.Equal("qwen:latest", decision.SelectedModel);
        Assert.Equal("Explain this.", decision.RoutedPrompt);
    }

    [Fact]
    public async Task RouterRejectsUnknownExplicitOverrideWithFallbackReason()
    {
        var router = new DeterministicChatModelRouter();
        var decision = await router.SelectModelAsync(new ModelRoutingRequest(
            "Explain this.",
            [],
            ["gemma4"],
            ModelRoutingSettings.CreateDefaultAssignments())
        {
            ExplicitModelOverride = "missing"
        });

        Assert.False(decision.ExplicitOverrideHonored);
        Assert.Equal("missing", decision.SelectedModel);
        Assert.True(decision.IsCapabilityBlocked);
        Assert.Contains("not installed", decision.FallbackReason);
    }

    [Fact]
    public async Task RouterNeverRoutesImagesToTextOnlyFallback()
    {
        var router = new DeterministicChatModelRouter();
        var decision = await router.SelectModelAsync(new ModelRoutingRequest(
            "Describe screenshot",
            [new AttachedContextSignal("Screenshot", HasImage: true)],
            ["text-only", "gemma4:latest"],
            [
                new ModelRoutingAssignment(ModelRoutingCategory.General, "text-only"),
                new ModelRoutingAssignment(ModelRoutingCategory.Coding, "text-only"),
                new ModelRoutingAssignment(ModelRoutingCategory.Vision, "missing"),
                new ModelRoutingAssignment(ModelRoutingCategory.Fast, "text-only"),
                new ModelRoutingAssignment(ModelRoutingCategory.Reasoning, "text-only")
            ]));

        Assert.Equal("gemma4:latest", decision.SelectedModel);
        Assert.Equal(ModelRoutingCategory.Vision, decision.Category);
    }

    [Fact]
    public async Task RouterFallsBackWhenInstalledVisionAssignmentIsTextOnly()
    {
        var router = new DeterministicChatModelRouter();
        var decision = await router.SelectModelAsync(new ModelRoutingRequest(
            "Describe screenshot",
            [new AttachedContextSignal("Screenshot", HasImage: true)],
            ["qwen3:8b", "qwen3-vl:8b"],
            [
                new ModelRoutingAssignment(ModelRoutingCategory.General, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Coding, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Vision, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Fast, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Reasoning, "qwen3:8b")
            ]));

        Assert.Equal("qwen3-vl:8b", decision.SelectedModel);
        Assert.Equal(ModelRoutingCategory.Vision, decision.Category);
        Assert.Contains("cannot accept images", decision.FallbackReason);
    }

    [Fact]
    public async Task RouterBlocksExplicitTextOnlyOverrideForImageRequest()
    {
        var router = new DeterministicChatModelRouter();
        var decision = await router.SelectModelAsync(new ModelRoutingRequest(
            "What is in this image?",
            [new AttachedContextSignal("Screenshot", HasImage: true)],
            ["qwen3:8b", "qwen3-vl:8b"],
            [
                new ModelRoutingAssignment(ModelRoutingCategory.General, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Coding, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Vision, "qwen3-vl:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Fast, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Reasoning, "qwen3:8b")
            ])
        {
            ExplicitModelOverride = "qwen3:8b"
        });

        Assert.True(decision.IsCapabilityBlocked);
        Assert.Equal("qwen3:8b", decision.SelectedModel);
        Assert.Equal("What is in this image?", decision.RoutedPrompt);
    }

    [Fact]
    public async Task RouterUsesConversationOverrideBeforeAutomaticRouting()
    {
        var router = new DeterministicChatModelRouter();
        var decision = await router.SelectModelAsync(new ModelRoutingRequest(
            "Write a summary",
            [],
            ["gemma4:12b", "qwen3:8b"],
            [
                new ModelRoutingAssignment(ModelRoutingCategory.General, "gemma4:12b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Coding, "gemma4:12b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Vision, "gemma4:12b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Fast, "gemma4:12b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Reasoning, "gemma4:12b")
            ])
        {
            ConversationModelOverride = "qwen3:8b"
        });

        Assert.Equal("qwen3:8b", decision.SelectedModel);
        Assert.Equal(ModelRoutingSource.ConversationOverride, decision.Source);
        Assert.Equal("Conversation override · qwen3:8b", decision.UserVisibleReason);
    }

    [Fact]
    public async Task RouterAllowsVisionCapableConversationOverrideForImage()
    {
        var router = new DeterministicChatModelRouter();
        var decision = await router.SelectModelAsync(new ModelRoutingRequest(
            "What is in this image?",
            [new AttachedContextSignal("Screenshot", HasImage: true)],
            ["gemma4:12b", "qwen3-vl:8b"],
            [
                new ModelRoutingAssignment(ModelRoutingCategory.General, "gemma4:12b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Coding, "gemma4:12b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Vision, "gemma4:12b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Fast, "gemma4:12b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Reasoning, "gemma4:12b")
            ])
        {
            ConversationModelOverride = "qwen3-vl:8b"
        });

        Assert.False(decision.IsCapabilityBlocked);
        Assert.Equal("qwen3-vl:8b", decision.SelectedModel);
        Assert.Equal(ModelRoutingSource.ConversationOverride, decision.Source);
    }

    [Fact]
    public async Task RouterUsesSettingsVisionAssignmentInAutomaticMode()
    {
        var router = new DeterministicChatModelRouter();
        var decision = await router.SelectModelAsync(new ModelRoutingRequest(
            "What is in this image?",
            [new AttachedContextSignal("Screenshot", HasImage: true)],
            ["gemma4:12b", "qwen3-vl:8b"],
            [
                new ModelRoutingAssignment(ModelRoutingCategory.General, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Coding, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Vision, "gemma4:12b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Fast, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Reasoning, "qwen3:8b")
            ]));

        Assert.Equal("gemma4:12b", decision.SelectedModel);
        Assert.Equal("Automatic · Vision: gemma4:12b", decision.UserVisibleReason);
    }

    [Fact]
    public async Task RouterAutomaticModeMayResolveQwenVisionModel()
    {
        var router = new DeterministicChatModelRouter();
        var decision = await router.SelectModelAsync(new ModelRoutingRequest(
            "What is in this image?",
            [new AttachedContextSignal("Screenshot", HasImage: true)],
            ["gemma4:12b", "qwen3-vl:8b"],
            [
                new ModelRoutingAssignment(ModelRoutingCategory.General, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Coding, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Vision, "qwen3-vl:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Fast, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Reasoning, "qwen3:8b")
            ]));

        Assert.Equal("qwen3-vl:8b", decision.SelectedModel);
        Assert.Equal("Automatic · Vision: qwen3-vl:8b", decision.UserVisibleReason);
    }

    [Fact]
    public async Task RouterBlocksImageRequestWhenNoInstalledVisionModelExists()
    {
        var router = new DeterministicChatModelRouter();
        var decision = await router.SelectModelAsync(new ModelRoutingRequest(
            "What is in this image?",
            [new AttachedContextSignal("Screenshot", HasImage: true)],
            ["qwen3:8b"],
            [
                new ModelRoutingAssignment(ModelRoutingCategory.General, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Coding, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Vision, "qwen3-vl:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Fast, "qwen3:8b"),
                new ModelRoutingAssignment(ModelRoutingCategory.Reasoning, "qwen3:8b")
            ]));

        Assert.True(decision.IsCapabilityBlocked);
        Assert.Contains("No installed vision-capable model", decision.FallbackReason);
    }

    [Fact]
    public void SettingsNormalizationRejectsTextOnlyVisionAssignment()
    {
        var settings = ApplicationSettings.CreateDefault() with
        {
            Models = new ModelSettings(
                [
                    new ModelRoutingAssignment(ModelRoutingCategory.General, "qwen3:8b"),
                    new ModelRoutingAssignment(ModelRoutingCategory.Coding, "qwen3:8b"),
                    new ModelRoutingAssignment(ModelRoutingCategory.Vision, "qwen3:8b"),
                    new ModelRoutingAssignment(ModelRoutingCategory.Fast, "qwen3:8b"),
                    new ModelRoutingAssignment(ModelRoutingCategory.Reasoning, "qwen3:8b")
                ])
        };

        var normalized = ApplicationSettingsValidator.Normalize(settings);

        Assert.Contains(normalized.Models.Assignments, assignment =>
            assignment.Category == ModelRoutingCategory.Vision &&
            assignment.Model == ModelRoutingSettings.DefaultModel);
    }

    [Fact]
    public async Task JsonSettingsServiceRoundTripsSettings()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "PersonalAI.Tests",
            Guid.NewGuid().ToString("N"),
            "settings.json");
        var service = new JsonApplicationSettingsService(path);
        await service.InitializeAsync();

        var updated = service.Current with
        {
            Models = new ModelSettings(
                [
                    new ModelRoutingAssignment(
                        ModelRoutingCategory.General,
                        "llama3")
                ]),
            Appearance = service.Current.Appearance with
            {
                Theme = ThemePreference.Dark
            }
        };

        await service.SaveAsync(updated);

        var reloaded = new JsonApplicationSettingsService(path);
        await reloaded.InitializeAsync();

        Assert.Contains(reloaded.Current.Models.Assignments, assignment =>
            assignment.Category == ModelRoutingCategory.General &&
            assignment.Model == "llama3");
        Assert.Equal(ThemePreference.Dark, reloaded.Current.Appearance.Theme);
    }

    [Fact]
    public async Task JsonSettingsServiceRecoversFromCorruptJson()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "PersonalAI.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(path, "{ not json");

        var service = new JsonApplicationSettingsService(path);
        await service.InitializeAsync();

        Assert.Contains(service.Current.Models.Assignments, assignment =>
            assignment.Category == ModelRoutingCategory.General &&
            assignment.Model == "gemma4");
    }

    [Fact]
    public async Task JsonSettingsServiceMigratesOldModelSettingsShape()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "PersonalAI.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "SchemaVersion": 1,
              "General": {
                "LaunchDestination": 0,
                "PreserveComposerDraftBetweenHideShow": true,
                "ConfirmBeforeClearingAllContext": true
              },
              "Appearance": {
                "Theme": 2,
                "CompactSidebar": true,
                "ShowMessageMetadata": false
              },
              "Models": {
                "DefaultChatModel": "old-text",
                "DefaultVisionModel": "old-vision",
                "RememberLastSelectedModel": true,
                "LastSelectedModel": "old-last"
              },
              "Hotkey": {
                "Control": true,
                "Alt": true,
                "Shift": false,
                "Windows": false,
                "Key": "Space"
              },
              "Window": {
                "CloseBehavior": 0,
                "StartMinimizedToTray": false,
                "RememberWindowPosition": true,
                "LaunchAtSignIn": false
              },
              "Context": {
                "MaxTotalTextContextCharacters": 24000,
                "MaxIndividualClipboardCharacters": 12000,
                "MaxAttachedContextItems": 8,
                "ScreenshotMaxPayloadBytes": 4194304,
                "ScreenshotThumbnailMaxEdge": 240,
                "ClearAttachmentsAfterSuccessfulSend": true
              },
              "Privacy": {
                "ExcludedApplications": [],
                "IncludeExecutablePathInProviderMetadata": false,
                "IncludeWindowTitleInProviderContext": true
              },
              "Vision": {
                "UserModelPatterns": []
              }
            }
            """);

        var service = new JsonApplicationSettingsService(path);
        await service.InitializeAsync();

        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, service.Current.SchemaVersion);
        Assert.True(service.Current.Appearance.CompactSidebar);
        Assert.Equal(MemoryRagSettings.Default, service.Current.MemoryRag);
        Assert.Contains(service.Current.Models.Assignments, assignment =>
            assignment.Category == ModelRoutingCategory.General &&
            assignment.Model == "gemma4");
    }
}
