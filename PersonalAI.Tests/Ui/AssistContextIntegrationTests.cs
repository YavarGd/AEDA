using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Desktop.WinUI.ViewModels;

namespace PersonalAI.Tests.Ui;

public sealed class AssistContextIntegrationTests
{
    [Fact]
    public async Task OpenPrompt_StoresEnvelopeAndPreviewOnHost()
    {
        var host = new IntegrationFakeHost
        {
            Context = ContextItem(42),
            Envelope = CreateEnvelope(),
            Chunks = ["Answer"]
        };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();
        await viewModel.WaitForGenerationAsync();

        Assert.NotNull(host.StoredEnvelope);
        Assert.Equal("notepad", host.StoredEnvelope!.ApplicationLabel);
        Assert.False(viewModel.ContextPreview.IsEmpty);
        Assert.Equal("ApplicationWindow", viewModel.ContextPreview.ContextType);
    }

    [Fact]
    public async Task OpenPrompt_WhenNoContext_StoresNullEnvelope()
    {
        var host = new IntegrationFakeHost { Context = null };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();

        Assert.Null(host.StoredEnvelope);
        Assert.True(viewModel.ContextPreview.IsEmpty);
    }

    [Fact]
    public async Task CopyResponse_RequestsFocusRestoration()
    {
        var host = new IntegrationFakeHost
        {
            Context = ContextItem(42),
            Envelope = CreateEnvelope(),
            Chunks = ["Answer"]
        };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();
        await viewModel.WaitForGenerationAsync();
        await viewModel.CopyResponseAsync();

        Assert.NotNull(host.LastFocusRestorationRequest);
        Assert.Equal(
            FocusRestorationTrigger.CopyResponse,
            host.LastFocusRestorationRequest!.Trigger);
    }

    [Fact]
    public async Task Collapse_RequestsFocusRestoration()
    {
        var host = new IntegrationFakeHost { Context = null };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();
        viewModel.Collapse();

        Assert.NotNull(host.LastFocusRestorationRequest);
        Assert.Equal(
            FocusRestorationTrigger.Dismiss,
            host.LastFocusRestorationRequest!.Trigger);
    }

    [Fact]
    public async Task Collapse_WhileStreaming_CancelsAndRequestsRestoration()
    {
        var host = new IntegrationFakeHost
        {
            Context = ContextItem(42),
            Envelope = CreateEnvelope(),
            WaitForCancellation = true
        };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();
        await host.GenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Collapse();

        Assert.True(host.GenerationWasCancelled);
        Assert.Equal(AssistPillState.IdlePill, viewModel.State);
    }

    [Fact]
    public async Task OpenInAedaAsync_RequestsAppOpenRestoration()
    {
        var host = new IntegrationFakeHost
        {
            Context = ContextItem(42),
            Envelope = CreateEnvelope(),
            Chunks = ["Answer"]
        };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();
        await viewModel.WaitForGenerationAsync();
        await viewModel.OpenInAedaAsync();

        Assert.Equal(AssistPillState.Hidden, viewModel.State);
        Assert.Equal(1, host.OpenCalls);
    }

    [Fact]
    public async Task OpenInAedaAsync_DoesNotRequestFocusRestoration()
    {
        var host = new IntegrationFakeHost { Context = ContextItem(42) };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();
        await viewModel.OpenInAedaAsync();

        Assert.Null(host.LastFocusRestorationRequest);
    }

    [Fact]
    public async Task HandoffToModule_DoesNotRequestFocusRestoration()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);
        var host = new IntegrationFakeHost
        {
            Context = ContextItem(42),
            Envelope = CreateEnvelope(),
            Chunks = ["Answer"],
            HandoffService = service
        };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();
        await viewModel.WaitForGenerationAsync();
        viewModel.HandoffToModule(AssistHandoffDestination.AedaCode);

        Assert.Null(host.LastFocusRestorationRequest);
        Assert.True(store.TryRead(out _));
    }

    [Fact]
    public async Task PreviewModel_UpdatesAfterContextCapture()
    {
        var blocked = AssistContextEnvelope.Blocked("privacy-blocked", "1password");
        var host = new IntegrationFakeHost
        {
            Context = null,
            Envelope = blocked
        };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();

        Assert.True(viewModel.ContextPreview.IsBlocked);
        Assert.Equal("privacy-blocked", viewModel.ContextPreview.BlockedReason);
        Assert.False(viewModel.ContextPreview.IsClearable);
    }

    [Fact]
    public async Task CopyResponse_WithNullForeground_DoesNotRequestRestoration()
    {
        var host = new IntegrationFakeHost
        {
            Context = ContextItem(42),
            Envelope = CreateEnvelope(),
            Chunks = ["Answer"],
            Foreground = null
        };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();
        await viewModel.WaitForGenerationAsync();
        await viewModel.CopyResponseAsync();

        Assert.NotNull(host.LastFocusRestorationRequest);
        Assert.False(host.LastFocusRestorationRequest!.ShouldRestore);
    }

    [Fact]
    public async Task MeaningfulContext_UsesAutomaticPromptAndStoresEnvelope()
    {
        var envelope = CreateEnvelope();
        var host = new IntegrationFakeHost
        {
            Context = ContextItem(50),
            Envelope = envelope,
            Chunks = ["Result"]
        };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();
        await viewModel.WaitForGenerationAsync();

        Assert.Equal(
            AssistPillViewModel.AutomaticContextPrompt,
            host.GeneratedPrompt);
        Assert.Equal("Completed", viewModel.StatusText);
        Assert.False(viewModel.ContextPreview.IsEmpty);
        Assert.Equal("notepad", viewModel.ContextPreview.ApplicationLabel);
    }

    [Fact]
    public async Task HandoffToModule_StoresPayloadInServiceWithRealContext()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);
        var envelope = CreateEnvelope();
        var host = new IntegrationFakeHost
        {
            Context = ContextItem(42),
            Envelope = envelope,
            Chunks = ["Answer"],
            HandoffService = service
        };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();
        await viewModel.WaitForGenerationAsync();
        viewModel.HandoffToModule(AssistHandoffDestination.AedaCode);

        var consumed = store.TryConsume();
        Assert.NotNull(consumed);
        Assert.Equal("Explain the selected content clearly and concisely.", consumed.UserRequest);
        Assert.True(consumed.HasContext);
        Assert.Same(envelope, consumed.Context);
        Assert.Equal(AssistHandoffDestination.AedaCode, consumed.Destination);
        Assert.Equal(1, host.HandoffCalls);
    }

    [Fact]
    public async Task HandoffToModule_AllDestinations_Work()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);
        var envelope = CreateEnvelope();
        var host = new IntegrationFakeHost
        {
            Context = ContextItem(42),
            Envelope = envelope,
            Chunks = ["Answer"],
            HandoffService = service
        };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();
        await viewModel.WaitForGenerationAsync();

        foreach (var dest in Enum.GetValues<AssistHandoffDestination>())
        {
            viewModel.HandoffToModule(dest);
            Assert.True(store.TryRead(out var stored));
            Assert.Equal(dest, stored!.Destination);
            Assert.True(stored.HasContext);
            store.Clear();
        }
    }

    [Fact]
    public async Task HandoffToModule_WithoutResponse_DoesNotStore()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);
        var host = new IntegrationFakeHost
        {
            Context = null,
            HandoffService = service
        };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();

        viewModel.HandoffToModule(AssistHandoffDestination.Chat);

        Assert.False(store.TryRead(out _));
    }

    [Fact]
    public async Task NoContext_ShowsFallbackAndPreviewIsEmpty()
    {
        var host = new IntegrationFakeHost { Context = null };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();

        Assert.True(viewModel.IsFallbackInput);
        Assert.True(viewModel.ContextPreview.IsEmpty);
        Assert.Equal(0, host.GenerateCalls);
    }

    [Fact]
    public void FocusRestorationPolicy_IntegrationWithHost()
    {
        var foreground = new ActiveWindowReference(
            100, 42, "notepad", "notes.txt", DateTimeOffset.UtcNow);
        var host = new IntegrationFakeHost { Foreground = foreground };

        var dismissRequest = host.RequestFocusRestoration(
            FocusRestorationTrigger.Dismiss);
        Assert.True(dismissRequest.ShouldRestore);
        Assert.Same(foreground, dismissRequest.PreviousForeground);

        var moduleRequest = host.RequestFocusRestoration(
            FocusRestorationTrigger.ModuleOpen);
        Assert.False(moduleRequest.ShouldRestore);

        var appRequest = host.RequestFocusRestoration(
            FocusRestorationTrigger.AppOpen);
        Assert.False(appRequest.ShouldRestore);
    }

    [Fact]
    public void HandoffStoreAndService_WorkTogether()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);
        var envelope = CreateEnvelope();

        service.Handoff(
            "Explain",
            envelope,
            "notepad",
            "Here",
            Guid.NewGuid(),
            AssistHandoffDestination.Chat);

        var consumed = service.TryConsume();
        Assert.NotNull(consumed);
        Assert.True(consumed.HasContext);
        Assert.Equal(AssistHandoffDestination.Chat, consumed.Destination);

        Assert.Null(service.TryConsume());
    }

    [Fact]
    public void Handoff_Chat_PreservesRequestAndContext()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);
        var envelope = CreateEnvelope();
        var conversationId = Guid.NewGuid();

        service.Handoff(
            "Explain this code",
            envelope,
            "notepad",
            "Here is the explanation",
            conversationId,
            AssistHandoffDestination.Chat);

        var consumed = service.TryConsume();
        Assert.NotNull(consumed);
        Assert.Equal("Explain this code", consumed.UserRequest);
        Assert.True(consumed.HasContext);
        Assert.Same(envelope, consumed.Context);
        Assert.Equal("notepad", consumed.OriginApplication);
        Assert.Equal("Here is the explanation", consumed.CurrentResponse);
        Assert.Equal(conversationId, consumed.ConversationId);
        Assert.Equal(AssistHandoffDestination.Chat, consumed.Destination);
    }

    [Fact]
    public void Handoff_Code_PreservesRequestAndContext()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);
        var envelope = CreateEnvelope();

        service.Handoff(
            "Fix this bug",
            envelope,
            "VS Code",
            null,
            Guid.NewGuid(),
            AssistHandoffDestination.AedaCode);

        var consumed = service.TryConsume();
        Assert.NotNull(consumed);
        Assert.Equal(AssistHandoffDestination.AedaCode, consumed.Destination);
        Assert.Equal("Fix this bug", consumed.UserRequest);
        Assert.True(consumed.HasContext);
    }

    [Fact]
    public void Handoff_Research_PreservesRequestAndContext()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);
        var envelope = CreateEnvelope();

        service.Handoff(
            "Research this topic",
            envelope,
            "browser",
            null,
            Guid.NewGuid(),
            AssistHandoffDestination.AedaResearch);

        var consumed = service.TryConsume();
        Assert.NotNull(consumed);
        Assert.Equal(AssistHandoffDestination.AedaResearch, consumed.Destination);
        Assert.True(consumed.HasContext);
    }

    [Fact]
    public void Handoff_Memory_PreservesRequestAndContext()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);
        var envelope = CreateEnvelope();

        service.Handoff(
            "Remember this",
            envelope,
            "notepad",
            null,
            null,
            AssistHandoffDestination.AedaMemory);

        var consumed = service.TryConsume();
        Assert.NotNull(consumed);
        Assert.Equal(AssistHandoffDestination.AedaMemory, consumed.Destination);
        Assert.True(consumed.HasContext);
    }

    [Fact]
    public void Handoff_WithBlockedContext_PreservesBlockedStateInPayload()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);
        var blocked = AssistContextEnvelope.Blocked("password-control", "chrome");

        service.Handoff(
            "Help with login",
            blocked,
            "chrome",
            null,
            null,
            AssistHandoffDestination.Chat);

        var consumed = service.TryConsume();
        Assert.NotNull(consumed);
        Assert.False(consumed.HasContext);
        Assert.True(consumed.Context!.IsBlocked);
        Assert.Equal("password-control", consumed.Context.BlockedReason);
    }

    [Fact]
    public void HandoffPayload_SafeActivitySummary_NeverExposesRawText()
    {
        var envelope = new AssistContextEnvelope(
            "notepad",
            "notepad",
            "notes.txt",
            AssistContextKind.ApplicationWindow,
            "secret password text",
            20,
            SelectedTextCaptureSource.ClipboardCopyFallback,
            DateTimeOffset.UtcNow,
            IsTruncated: false,
            IsBlocked: false,
            BlockedReason: null,
            UnderlyingItem: null,
            new Dictionary<string, string>());

        var payload = new AssistHandoffPayload(
            "Copy this",
            envelope,
            "notepad",
            "Copied",
            Guid.NewGuid(),
            AssistHandoffDestination.Chat);

        var summary = payload.SafeActivitySummary;
        Assert.DoesNotContain("secret", summary);
        Assert.DoesNotContain("password", summary);
        Assert.Contains("20 chars", summary);
    }

    [Fact]
    public async Task OpenInAedaAsync_DoesNotStoreHandoffPayload()
    {
        var store = new AssistHandoffStore();
        var service = new AssistHandoffService(store);
        var host = new IntegrationFakeHost
        {
            Context = ContextItem(42),
            Envelope = CreateEnvelope(),
            Chunks = ["Answer"],
            HandoffService = service
        };
        var viewModel = new AssistPillViewModel(
            host,
            new AssistPillSettings(true, 1_200));

        await viewModel.OpenPromptAsync();
        await viewModel.WaitForGenerationAsync();
        await viewModel.OpenInAedaAsync();

        Assert.False(store.TryRead(out _));
    }

    private static AttachedContextItem ContextItem(int selectedCharacters) =>
        new(
            Guid.NewGuid(),
            AttachedContextType.ApplicationWindow,
            "Editor",
            "Document",
            "Editor Document",
            "Attached active-window context",
            Images: [],
            ThumbnailDataUri: null,
            new Dictionary<string, string>
            {
                ["selectedTextCharacters"] = selectedCharacters.ToString()
            },
            DateTimeOffset.UtcNow,
            Guid.NewGuid().ToString("N"));

    private static AssistContextEnvelope CreateEnvelope() => new(
        "notepad",
        "notepad",
        "notes.txt",
        AssistContextKind.ApplicationWindow,
        "selected text",
        13,
        SelectedTextCaptureSource.UiAutomationTextPattern,
        DateTimeOffset.UtcNow,
        IsTruncated: false,
        IsBlocked: false,
        BlockedReason: null,
        UnderlyingItem: null,
        new Dictionary<string, string>());

    private sealed class IntegrationFakeHost : IAssistPillHost
    {
        private readonly TaskCompletionSource _captureReleased =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AttachedContextItem? Context { get; set; }
        public AssistContextEnvelope? Envelope { get; set; }
        public ActiveWindowReference? Foreground { get; set; } = new(
            100, 42, "notepad", "notes.txt", DateTimeOffset.UtcNow);
        public string? LastCaptureFailureMessage { get; set; }
        public IReadOnlyList<string> Chunks { get; set; } = [];
        public AssistGenerationResult Result { get; set; } = new(ChatStatus.Completed);
        public bool WaitForCapture { get; set; }
        public bool WaitForCancellation { get; set; }
        public bool GenerationWasCancelled { get; private set; }
        public int GenerateCalls { get; private set; }
        public int OpenCalls { get; private set; }
        public int HandoffCalls { get; private set; }
        public string? GeneratedPrompt { get; private set; }
        public AssistContextEnvelope? StoredEnvelope { get; private set; }
        public FocusRestorationRequest? LastFocusRestorationRequest { get; private set; }
        public AssistHandoffDestination? LastHandoffDestination { get; private set; }
        public string? LastHandoffRequest { get; private set; }
        public AssistHandoffService? HandoffService { get; set; }
        public TaskCompletionSource CaptureStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource GenerationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AssistContextEnvelope? CurrentEnvelope => StoredEnvelope;

        public ActiveWindowReference? CapturedForeground => Foreground;

        public Task<AttachedContextItem?> CaptureContextAsync(
            CancellationToken cancellationToken)
        {
            CaptureStarted.TrySetResult();
            StoredEnvelope = Envelope;
            return Task.FromResult(Context);
        }

        public Task<AttachedContextItem?> CaptureScreenTextAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<AttachedContextItem?>(null);

        public async Task<AssistGenerationResult> GenerateAsync(
            string prompt,
            AttachedContextItem? context,
            Action<string> reportChunk,
            CancellationToken cancellationToken)
        {
            GenerateCalls++;
            GeneratedPrompt = prompt;
            GenerationStarted.TrySetResult();

            foreach (var chunk in Chunks)
            {
                reportChunk(chunk);
            }

            if (WaitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    GenerationWasCancelled = true;
                    throw;
                }
            }

            return Result;
        }

        public Task CopyTextAsync(string text, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public FocusRestorationRequest RequestFocusRestoration(
            FocusRestorationTrigger trigger)
        {
            var request = AssistFocusRestorationPolicy.CreateRequest(
                Foreground, trigger);
            LastFocusRestorationRequest = request;
            return request;
        }

        public Task OpenInAedaAsync()
        {
            OpenCalls++;
            return Task.CompletedTask;
        }

        public void HandoffToModule(
            string userRequest,
            AssistHandoffDestination destination)
        {
            HandoffCalls++;
            LastHandoffRequest = userRequest;
            LastHandoffDestination = destination;
            HandoffService?.Handoff(
                userRequest,
                StoredEnvelope,
                Foreground?.ProcessName,
                null,
                null,
                destination);
        }
    }
}
