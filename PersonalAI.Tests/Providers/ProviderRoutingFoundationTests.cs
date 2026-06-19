using System.Text.Json;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Providers;
using PersonalAI.Core.Settings;

namespace PersonalAI.Tests.Providers;

public sealed class ProviderRoutingFoundationTests
{
    [Theory]
    [InlineData("http://localhost:11434", ProviderEndpointOrigin.Local)]
    [InlineData("http://127.0.0.1:3001/v1", ProviderEndpointOrigin.Local)]
    [InlineData("http://[::1]:3001/v1", ProviderEndpointOrigin.Local)]
    [InlineData("http://192.168.1.10:3001/v1", ProviderEndpointOrigin.PrivateNetwork)]
    [InlineData("https://api.example.com/v1", ProviderEndpointOrigin.Remote)]
    public void Classify_ReturnsExpectedOrigin(
        string endpoint,
        ProviderEndpointOrigin expected)
    {
        var classified = ProviderEndpointClassifier.Classify(endpoint);

        Assert.Equal(expected, classified.Origin);
        Assert.True(classified.IsUsable);
    }

    [Fact]
    public void Classify_RejectsInvalidUrlSafely()
    {
        var classified = ProviderEndpointClassifier.Classify("not a url");

        Assert.Equal(ProviderEndpointOrigin.Invalid, classified.Origin);
        Assert.False(classified.IsUsable);
        Assert.Equal("provider_endpoint_invalid", classified.SafeReasonCode);
    }

    [Fact]
    public void Classify_RejectsCredentialBearingUrl()
    {
        var classified = ProviderEndpointClassifier.Classify(
            "https://user:secret@example.com/v1");

        Assert.Equal(ProviderEndpointOrigin.Invalid, classified.Origin);
        Assert.Equal(
            "provider_endpoint_credentials_not_allowed",
            classified.SafeReasonCode);
        Assert.DoesNotContain(
            "secret",
            ProviderEndpointClassifier.SafeDisplay(classified),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InMemorySecretStore_SetGetDelete_AndDoesNotRevealToString()
    {
        var store = new InMemorySecretStore();
        await store.SetAsync("openai-compatible/test", new SecretValue("sk-test"));

        Assert.True(await store.ExistsAsync("openai-compatible/test"));
        Assert.Equal("sk-test", (await store.GetAsync("openai-compatible/test"))!.Value);
        Assert.DoesNotContain("sk-test", (await store.GetAsync("openai-compatible/test"))!.ToString());

        await store.DeleteAsync("openai-compatible/test");

        Assert.False(await store.ExistsAsync("openai-compatible/test"));
        Assert.Null(await store.GetAsync("openai-compatible/test"));
    }

    [Fact]
    public void Settings_DefaultsRemainLocalOnlyAndDoNotSerializeSecrets()
    {
        var settings = ApplicationSettings.CreateDefault() with
        {
            ProviderRouting = ProviderRoutingSettings.Default with
            {
                ProviderProfiles =
                [
                    new ProviderProfileSetting(
                        "remote",
                        ProviderKind.OpenAICompatible,
                        "Remote",
                        "https://api.example.com/v1",
                        IsEnabled: true,
                        ChatModel: "gpt-test",
                        EmbeddingModel: "embed-test",
                        SecretReference: "openai-compatible/remote")
                ],
                LocalOnlyMode = true,
                AllowRemoteChat = true,
                AllowRemoteEmbeddings = true
            }
        };

        var normalized = ApplicationSettingsValidator.Normalize(settings);
        var json = JsonSerializer.Serialize(normalized);

        Assert.True(normalized.ProviderRouting.LocalOnlyMode);
        Assert.False(normalized.ProviderRouting.AllowRemoteChat);
        Assert.False(normalized.ProviderRouting.AllowRemoteEmbeddings);
        Assert.DoesNotContain("sk-", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("openai-compatibleremote", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoutingPolicy_SelectsLocalDefault()
    {
        var registry = new StaticProviderRegistry([CreateProvider("ollama", local: true)]);
        var policy = new LocalFirstModelRoutingPolicy(registry);

        var decision = await policy.SelectAsync(new ModelRoutingPolicyRequest(
            ModelCapability.Chat,
            LocalOnlyMode: true,
            AllowRemoteChat: false,
            AllowRemoteEmbeddings: false,
            AllowRemoteWorkspaceContext: false,
            AllowRemoteMemoryContext: false,
            AllowRemoteScreenshots: false,
            AllowRemoteClipboardOrAppContext: false,
            IncludesWorkspaceContent: false,
            IncludesMemoryContext: false,
            IncludesScreenshot: false,
            IncludesClipboardOrAppContext: false));

        Assert.True(decision.IsAllowed);
        Assert.Equal("ollama", decision.Provider!.Id.Value);
    }

    [Theory]
    [InlineData(true, false, false, false, "remote_workspace_context_denied")]
    [InlineData(false, true, false, false, "remote_memory_context_denied")]
    [InlineData(false, false, true, false, "remote_screenshot_context_denied")]
    [InlineData(false, false, false, true, "remote_clipboard_app_context_denied")]
    public async Task RoutingPolicy_DeniesRemoteSensitiveContextByDefault(
        bool workspace,
        bool memory,
        bool screenshot,
        bool clipboard,
        string expectedReason)
    {
        var remote = CreateProvider("remote", local: false);
        var registry = new StaticProviderRegistry([remote]);
        var policy = new LocalFirstModelRoutingPolicy(registry);

        var decision = await policy.SelectAsync(new ModelRoutingPolicyRequest(
            ModelCapability.Chat,
            LocalOnlyMode: false,
            AllowRemoteChat: true,
            AllowRemoteEmbeddings: false,
            AllowRemoteWorkspaceContext: false,
            AllowRemoteMemoryContext: false,
            AllowRemoteScreenshots: false,
            AllowRemoteClipboardOrAppContext: false,
            IncludesWorkspaceContent: workspace,
            IncludesMemoryContext: memory,
            IncludesScreenshot: screenshot,
            IncludesClipboardOrAppContext: clipboard,
            ProviderOverride: remote.Id));

        Assert.False(decision.IsAllowed);
        Assert.Equal(expectedReason, decision.SafeReasonCode);
    }

    [Fact]
    public async Task RoutingPolicy_ExplicitAllowPermitsRemoteContext()
    {
        var remote = CreateProvider("remote", local: false);
        var policy = new LocalFirstModelRoutingPolicy(
            new StaticProviderRegistry([remote]));

        var decision = await policy.SelectAsync(new ModelRoutingPolicyRequest(
            ModelCapability.Chat,
            LocalOnlyMode: false,
            AllowRemoteChat: true,
            AllowRemoteEmbeddings: false,
            AllowRemoteWorkspaceContext: true,
            AllowRemoteMemoryContext: true,
            AllowRemoteScreenshots: true,
            AllowRemoteClipboardOrAppContext: true,
            IncludesWorkspaceContent: true,
            IncludesMemoryContext: true,
            IncludesScreenshot: true,
            IncludesClipboardOrAppContext: true,
            ProviderOverride: remote.Id));

        Assert.True(decision.IsAllowed);
        Assert.False(decision.RequiresExplicitApproval);
    }

    [Fact]
    public async Task RoutingPolicy_UnhealthyProviderIsNotSelected()
    {
        var provider = CreateProvider("remote", local: false);
        var registry = new StaticProviderRegistry(
            [provider],
            new Dictionary<ProviderId, IProviderHealthProbe>
            {
                [provider.Id] = new FakeHealthProbe(
                    new ProviderHealth(ProviderStatus.Unavailable, "down"))
            });
        var policy = new LocalFirstModelRoutingPolicy(registry);

        var decision = await policy.SelectAsync(new ModelRoutingPolicyRequest(
            ModelCapability.Chat,
            LocalOnlyMode: false,
            AllowRemoteChat: true,
            AllowRemoteEmbeddings: false,
            AllowRemoteWorkspaceContext: true,
            AllowRemoteMemoryContext: true,
            AllowRemoteScreenshots: true,
            AllowRemoteClipboardOrAppContext: true,
            IncludesWorkspaceContent: false,
            IncludesMemoryContext: false,
            IncludesScreenshot: false,
            IncludesClipboardOrAppContext: false));

        Assert.False(decision.IsAllowed);
        Assert.Equal("provider_capability_unavailable", decision.SafeReasonCode);
    }

    [Fact]
    public async Task ContextPrivacyFilter_LocalKeepsContext_RemoteStripsSafely()
    {
        var local = CreateProvider("ollama", local: true);
        var remote = CreateProvider("remote", local: false);
        var filter = new ContextPrivacyFilter();
        var secretText = "project secret token";
        IReadOnlyList<ChatMessage> messages =
        [
            new(ChatRole.System, "[workspace] " + secretText),
            new(ChatRole.User, "[memory] remembered detail"),
            new(ChatRole.User, "[clipboard] copied detail")
        ];

        var localResult = await filter.FilterAsync(new ContextPrivacyFilterRequest(
            local,
            messages,
            false,
            false,
            false,
            false));
        var remoteResult = await filter.FilterAsync(new ContextPrivacyFilterRequest(
            remote,
            messages,
            false,
            false,
            false,
            false));

        Assert.Equal(messages.Count, localResult.Messages.Count);
        Assert.All(remoteResult.Messages, message => Assert.Equal(string.Empty, message.Content));
        Assert.DoesNotContain(
            secretText,
            string.Join(",", remoteResult.RemovedSafeSummaries),
            StringComparison.Ordinal);
        Assert.Contains("workspace_context_removed", remoteResult.RemovedSafeSummaries);
        Assert.Contains("memory_context_removed", remoteResult.RemovedSafeSummaries);
        Assert.Contains("clipboard_app_context_removed", remoteResult.RemovedSafeSummaries);
    }

    [Fact]
    public void ProviderRegistry_ListsConfiguredProvidersAndModels()
    {
        var provider = CreateProvider("ollama", local: true);
        var registry = new StaticProviderRegistry([provider]);

        Assert.True(registry.TryGetProvider(provider.Id, out _));
        Assert.Single(registry.ListProviders());
        Assert.Single(registry.ListModels(provider.Id));
        Assert.NotNull(registry.FindModel(provider.Id, new ModelId("model")));
    }

    private static ProviderProfile CreateProvider(string id, bool local)
    {
        var endpoint = ProviderEndpointClassifier.Classify(
            local ? "http://127.0.0.1:11434" : "https://api.example.com/v1");
        var providerId = new ProviderId(id);
        var capabilities = ModelCapability.Chat |
            ModelCapability.StreamingChat |
            (local ? ModelCapability.LocalOnly : ModelCapability.Remote);
        return new ProviderProfile(
            providerId,
            local ? ProviderKind.Ollama : ProviderKind.OpenAICompatible,
            id,
            endpoint,
            IsEnabled: true,
            ChatModel: "model",
            EmbeddingModel: null,
            SecretReference: null,
            Models:
            [
                new ModelDescriptor(
                    providerId,
                    new ModelId("model"),
                    capabilities,
                    new ModelSafetyProfile(
                        IsLocalOnly: local,
                        IsRemote: !local,
                        AllowsWorkspaceContext: local,
                        AllowsMemoryContext: local,
                        AllowsScreenshots: local,
                        AllowsClipboardOrAppContext: local),
                    "model")
            ]);
    }

    private sealed class FakeHealthProbe(ProviderHealth health) : IProviderHealthProbe
    {
        public Task<ProviderHealth> CheckAsync(
            ProviderProfile provider,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(health);
    }
}
