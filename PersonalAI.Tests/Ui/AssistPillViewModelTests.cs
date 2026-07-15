using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.WinUI.Models;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Desktop.WinUI.ViewModels;

namespace PersonalAI.Tests.Ui;

public sealed class AssistPillViewModelTests
{
    [Theory]
    [InlineData(true, AssistPillState.IdlePill)]
    [InlineData(false, AssistPillState.Hidden)]
    public void StartsAccordingToEnabledSetting(bool enabled, AssistPillState expected)
    {
        var viewModel = CreateViewModel(new FakeHost(), enabled);

        Assert.Equal(expected, viewModel.State);
    }

    [Fact]
    public async Task MeaningfulContext_StartsDirectGenerationWithoutFallback()
    {
        var host = new FakeHost
        {
            Context = Context(selectedCharacters: 42),
            Chunks = ["First ", "answer"]
        };
        var viewModel = CreateViewModel(host);

        Assert.True(await viewModel.OpenPromptAsync());
        await viewModel.WaitForGenerationAsync();

        Assert.Equal(AssistPillViewModel.AutomaticContextPrompt, host.GeneratedPrompt);
        Assert.Same(host.Context, host.GeneratedContext);
        Assert.False(viewModel.IsFallbackInput);
        Assert.Equal(AssistPillState.Completed, viewModel.State);
        Assert.Equal("First answer", viewModel.Response);
    }

    [Fact]
    public async Task MeaningfulContext_ShowsSelectionFeedbackWhileStreaming()
    {
        var host = new FakeHost
        {
            Context = Context(42),
            WaitForCancellation = true
        };
        var viewModel = CreateViewModel(host);

        await viewModel.OpenPromptAsync();
        await host.GenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(AssistPillState.StreamingResponse, viewModel.State);
        Assert.Equal("Using selected text", viewModel.StatusText);
        viewModel.Cancel();
        await viewModel.WaitForGenerationAsync();
    }

    [Fact]
    public async Task MetadataOnlyContext_UsesCompactFallbackAndDoesNotGenerate()
    {
        var host = new FakeHost { Context = Context(selectedCharacters: 0) };
        var viewModel = CreateViewModel(host);

        await viewModel.OpenPromptAsync();

        Assert.True(viewModel.IsFallbackInput);
        Assert.Equal(0, host.GenerateCalls);
    }

    [Fact]
    public async Task StaleContext_UsesFallback()
    {
        var host = new FakeHost
        {
            Context = Context(20) with { CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-3) }
        };
        var viewModel = CreateViewModel(host);

        await viewModel.OpenPromptAsync();

        Assert.True(viewModel.IsFallbackInput);
        Assert.Equal(0, host.GenerateCalls);
    }

    [Fact]
    public async Task NoContext_FallbackSubmissionHidesInputAndStreams()
    {
        var host = new FakeHost { Chunks = ["Visible answer"] };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "Help me reason about this";

        var generation = viewModel.SubmitAsync();
        Assert.False(viewModel.IsFallbackInput);
        await generation;

        Assert.Equal("Help me reason about this", host.GeneratedPrompt);
        Assert.Equal(AssistPillState.Completed, viewModel.State);
    }

    [Fact]
    public async Task NewInvocationClearsCompletedPresentationBeforeCaptureCompletes()
    {
        var host = new FakeHost { Context = Context(10), Chunks = ["Old answer"] };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        await viewModel.WaitForGenerationAsync();
        viewModel.Collapse();
        host.Context = null;
        host.WaitForCapture = true;

        var reopening = viewModel.OpenPromptAsync();

        Assert.Equal(AssistPillState.DetectingContext, viewModel.State);
        Assert.Empty(viewModel.Response);
        Assert.False(viewModel.CanShowResponseActions);
        host.ReleaseCapture();
        Assert.True(await reopening);
        Assert.True(viewModel.IsFallbackInput);
    }

    [Fact]
    public async Task LatePreviousStreamUpdateCannotChangeNewSpotlightInvocation()
    {
        var host = new FakeHost { Context = Context(10), Chunks = ["Old answer"] };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        await viewModel.WaitForGenerationAsync();
        var previousReport = host.LastReportChunk!;
        viewModel.Collapse();
        host.Context = null;
        await viewModel.OpenPromptAsync();

        previousReport(" stale");

        Assert.True(viewModel.IsFallbackInput);
        Assert.Empty(viewModel.Response);
        Assert.Equal("Ask AEDA", viewModel.StatusText);
    }

    [Fact]
    public async Task RepeatedOpenWhileCapturing_DoesNotDuplicateSubmission()
    {
        var host = new FakeHost
        {
            Context = Context(10),
            WaitForCapture = true,
            Chunks = ["Done"]
        };
        var viewModel = CreateViewModel(host);

        var first = viewModel.OpenPromptAsync();
        await host.CaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AssistPillState.DetectingContext, viewModel.State);
        Assert.True(viewModel.IsResponseSurface);
        Assert.False(await viewModel.OpenPromptAsync());
        host.ReleaseCapture();
        Assert.True(await first);
        await viewModel.WaitForGenerationAsync();

        Assert.Equal(1, host.CaptureCalls);
        Assert.Equal(1, host.GenerateCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(150)]
    public async Task RapidActivationDuringCapture_IsIgnored(int delayMilliseconds)
    {
        var host = new FakeHost
        {
            Context = Context(10),
            WaitForCapture = true,
            Chunks = ["Done"]
        };
        var viewModel = CreateViewModel(host);

        var first = viewModel.OpenPromptAsync();
        await host.CaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (delayMilliseconds > 0)
        {
            await Task.Delay(delayMilliseconds);
        }

        Assert.False(await viewModel.OpenPromptAsync());
        host.ReleaseCapture();
        Assert.True(await first);
        await viewModel.WaitForGenerationAsync();
        Assert.Equal(1, host.CaptureCalls);
        Assert.Equal(1, host.GenerateCalls);
    }

    [Fact]
    public async Task ActivationAfterCaptureCompletion_StartsNormally()
    {
        var host = new FakeHost { Context = null };
        var viewModel = CreateViewModel(host);

        Assert.True(await viewModel.OpenPromptAsync());
        viewModel.ShowIdle();
        Assert.True(await viewModel.OpenPromptAsync());

        Assert.Equal(2, host.CaptureCalls);
    }

    [Fact]
    public async Task CancellingContextDetectionRestoresIdleWithoutFallback()
    {
        var host = new FakeHost { WaitForCapture = true };
        var viewModel = CreateViewModel(host);
        using var cancellation = new CancellationTokenSource();

        var opening = viewModel.OpenPromptAsync(cancellation.Token);
        await host.CaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        Assert.False(await opening);
        Assert.Equal(AssistPillState.IdlePill, viewModel.State);
        Assert.Equal(0, host.GenerateCalls);
    }

    [Fact]
    public async Task SuccessfulEmptyStream_ProducesControlledFailure()
    {
        var viewModel = CreateViewModel(new FakeHost());
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "Answer";

        await viewModel.SubmitAsync();

        Assert.Equal(AssistPillState.Failed, viewModel.State);
        Assert.Equal("The provider returned no visible answer.", viewModel.StatusText);
    }

    [Fact]
    public async Task FullyFilteredStream_ProducesControlledFailureAndCannotCopyReasoning()
    {
        var host = new FakeHost { Chunks = ["<think>private reasoning</think>"] };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "Answer";

        await viewModel.SubmitAsync();
        await viewModel.CopyResponseAsync();

        Assert.Equal(AssistPillState.Failed, viewModel.State);
        Assert.Empty(viewModel.Response);
        Assert.Empty(host.CopiedText);
    }

    [Fact]
    public async Task StreamFailureAfterPartialAnswer_LeavesSafePartialContentRecoverable()
    {
        var host = new FakeHost
        {
            Chunks = ["Safe partial"],
            Failure = new InvalidOperationException("provider secret")
        };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "Answer";

        await viewModel.SubmitAsync();

        Assert.Equal(AssistPillState.Failed, viewModel.State);
        Assert.Equal("Safe partial", viewModel.Response);
        Assert.DoesNotContain("provider secret", viewModel.StatusText);
        viewModel.Collapse();
        Assert.Equal(AssistPillState.IdlePill, viewModel.State);
    }

    [Fact]
    public async Task ProviderFailureUsesSafeMessage()
    {
        var host = new FakeHost
        {
            Result = new AssistGenerationResult(
                ChatStatus.Failed,
                "The configured chat provider is unavailable.")
        };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "Answer";

        await viewModel.SubmitAsync();

        Assert.Equal(AssistPillState.Failed, viewModel.State);
        Assert.Equal("The configured chat provider is unavailable.", viewModel.StatusText);
    }

    [Fact]
    public async Task Retry_RechecksProviderAndContinuesThePendingRequest()
    {
        var host = new FakeHost
        {
            Result = new AssistGenerationResult(ChatStatus.Failed, "Ollama is not running.")
        };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "Answer";
        await viewModel.SubmitAsync();

        Assert.True(viewModel.CanRetry);
        host.Result = new AssistGenerationResult(ChatStatus.Completed);
        host.Chunks = ["Recovered"];
        await viewModel.RetryAsync();

        Assert.Equal(2, host.GenerateCalls);
        Assert.Equal(AssistPillState.Completed, viewModel.State);
        Assert.Equal("Recovered", viewModel.Response);
    }

    [Fact]
    public async Task Retry_DoesNotReuseStaleSelectedText()
    {
        var host = new FakeHost
        {
            Context = Context(20),
            Result = new AssistGenerationResult(ChatStatus.Failed, "Ollama is not running.")
        };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        await viewModel.WaitForGenerationAsync();
        host.Context = Context(20) with
        {
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-3)
        };

        await viewModel.RetryAsync();

        Assert.Equal(1, host.GenerateCalls);
        Assert.True(viewModel.IsFallbackInput);
        Assert.Equal("Ask AEDA", viewModel.StatusText);
    }

    [Fact]
    public async Task CancellationIsScopedAndNextInvocationCanSucceed()
    {
        var host = new FakeHost { WaitForCancellation = true };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "Long answer";
        var generation = viewModel.SubmitAsync();
        await host.GenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsStreaming);
        Assert.False(viewModel.IsFallbackInput);
        viewModel.Cancel();
        await generation;

        Assert.Equal(AssistPillState.Cancelled, viewModel.State);
        Assert.True(host.GenerationWasCancelled);
        host.WaitForCancellation = false;
        host.Chunks = ["Next answer"];
        viewModel.Collapse();
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "Try again";
        await viewModel.SubmitAsync();

        Assert.Equal(AssistPillState.Completed, viewModel.State);
        Assert.Equal(2, host.GenerateCalls);
    }

    [Fact]
    public async Task AutomaticFailureOffersScreenSelectionOrManualInput()
    {
        var viewModel = CreateViewModel(new FakeHost());

        await viewModel.OpenPromptAsync();

        Assert.True(viewModel.IsFallbackInput);
    }

    [Fact]
    public async Task ScreenTextSuccessSubmitsOnceThroughExistingFlow()
    {
        var screenContext = Context(24) with
        {
            Metadata = new Dictionary<string, string>
            {
                ["selectedTextCharacters"] = "24",
                ["captureSource"] = "screenOcr"
            }
        };
        var host = new FakeHost
        {
            ScreenContext = screenContext,
            WaitForCancellation = true
        };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();

        await viewModel.SelectScreenTextAsync();
        await host.GenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, host.ScreenCaptureCalls);
        Assert.Equal(1, host.GenerateCalls);
        Assert.Same(screenContext, host.GeneratedContext);
        Assert.Equal("Using text selected from screen", viewModel.StatusText);
        viewModel.Cancel();
        await viewModel.WaitForGenerationAsync();
    }

    [Fact]
    public async Task EmptyScreenTextOffersRetryOrManualWithoutGeneration()
    {
        var host = new FakeHost
        {
            LastCaptureFailureMessage = "No text was found in that area"
        };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();

        await viewModel.SelectScreenTextAsync();

        Assert.True(viewModel.IsFallbackInput);
        Assert.Equal("No text found — try another area", viewModel.StatusText);
        Assert.Equal(0, host.GenerateCalls);
    }

    [Fact]
    public async Task RepeatedScreenSelectionActionCreatesOneCapture()
    {
        var host = new FakeHost { WaitForScreenCapture = true };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();

        var first = viewModel.SelectScreenTextAsync();
        await host.ScreenCaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.SelectScreenTextAsync();
        host.ReleaseScreenCapture();
        await first;

        Assert.Equal(1, host.ScreenCaptureCalls);
    }

    [Fact]
    public async Task ScreenSelectionCanBeCancelledWithoutStartingGeneration()
    {
        var host = new FakeHost { WaitForScreenCapture = true };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();

        var capture = viewModel.SelectScreenTextAsync();
        await host.ScreenCaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.CancelContextCapture();
        await capture;

        Assert.True(viewModel.IsFallbackInput);
        Assert.Equal("Ask AEDA", viewModel.StatusText);
        Assert.Equal(0, host.GenerateCalls);
    }

    [Fact]
    public async Task RapidFallbackSubmissions_StartOneGeneration()
    {
        var host = new FakeHost { WaitForCancellation = true };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "One request";

        var first = viewModel.SubmitAsync();
        await host.GenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.SubmitAsync();
        viewModel.Cancel();
        await first;

        Assert.Equal(1, host.GenerateCalls);
    }

    [Fact]
    public async Task CopyAndOpenUseSafeResponseAndUnderlyingConversation()
    {
        var host = new FakeHost
        {
            Chunks = ["<analysis>hidden</analysis>Visible"]
        };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "Answer";
        await viewModel.SubmitAsync();

        await viewModel.CopyResponseAsync();
        await viewModel.OpenInAedaAsync();

        Assert.Equal("Visible", host.CopiedText);
        Assert.Equal(1, host.OpenCalls);
        Assert.Equal(AssistPillState.Hidden, viewModel.State);
    }

    [Fact]
    public void MeaningfulContextPolicy_RejectsClipboardAndFutureContext()
    {
        var now = DateTimeOffset.UtcNow;
        var selected = Context(5);

        Assert.True(AssistContextPolicy.IsMeaningful(selected, now));
        Assert.False(AssistContextPolicy.IsMeaningful(
            selected with { Type = AttachedContextType.Clipboard },
            now));
        Assert.False(AssistContextPolicy.IsMeaningful(
            selected with { CreatedAtUtc = now.AddMinutes(2) },
            now));
    }

    [Theory]
    [InlineData("Code", "Program.cs - repo - Visual Studio Code", true)]
    [InlineData("Code - Insiders", "Program.cs - repo - Visual Studio Code", true)]
    [InlineData("Code", "Other.cs - elsewhere - Visual Studio Code", false)]
    [InlineData("notepad", "Program.cs", false)]
    public void VsCodeContext_MustMatchForegroundEditor(
        string processName,
        string title,
        bool expected)
    {
        var context = Context(5) with
        {
            Type = AttachedContextType.VsCodeEditor,
            Metadata = new Dictionary<string, string>
            {
                ["selectedTextCharacters"] = "5",
                ["fileName"] = "Program.cs",
                ["workspace"] = "repo"
            }
        };
        var foreground = new ActiveWindowReference(
            1,
            2,
            processName,
            title,
            DateTimeOffset.UtcNow);

        Assert.Equal(expected, AssistContextPolicy.MatchesForeground(context, foreground));
    }

    private static AssistPillViewModel CreateViewModel(
        FakeHost host,
        bool enabled = true) =>
        new(host, new AssistPillSettings(enabled, 1_200));

    private static AttachedContextItem Context(int selectedCharacters) =>
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

    private sealed class FakeHost : IAssistPillHost
    {
        private readonly TaskCompletionSource _captureReleased =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AttachedContextItem? Context { get; set; }
        public AttachedContextItem? ScreenContext { get; set; }
        public string? LastCaptureFailureMessage { get; set; }
        public IReadOnlyList<string> Chunks { get; set; } = [];
        public Exception? Failure { get; init; }
        public AssistGenerationResult Result { get; set; } =
            new(ChatStatus.Completed);
        public bool WaitForCapture { get; set; }
        public bool WaitForScreenCapture { get; init; }
        public bool WaitForCancellation { get; set; }
        public bool GenerationWasCancelled { get; private set; }
        public int CaptureCalls { get; private set; }
        public int ScreenCaptureCalls { get; private set; }
        public int GenerateCalls { get; private set; }
        public int OpenCalls { get; private set; }
        public string? GeneratedPrompt { get; private set; }
        public AttachedContextItem? GeneratedContext { get; private set; }
        public Action<string>? LastReportChunk { get; private set; }
        public string CopiedText { get; private set; } = string.Empty;
        public TaskCompletionSource CaptureStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ScreenCaptureStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _screenCaptureReleased =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource GenerationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AttachedContextItem?> CaptureContextAsync(
            CancellationToken cancellationToken)
        {
            CaptureCalls++;
            CaptureStarted.TrySetResult();
            if (WaitForCapture)
            {
                await _captureReleased.Task.WaitAsync(cancellationToken);
            }

            return Context;
        }

        public async Task<AttachedContextItem?> CaptureScreenTextAsync(
            CancellationToken cancellationToken)
        {
            ScreenCaptureCalls++;
            ScreenCaptureStarted.TrySetResult();
            if (WaitForScreenCapture)
            {
                await _screenCaptureReleased.Task.WaitAsync(cancellationToken);
            }

            return ScreenContext;
        }

        public async Task<AssistGenerationResult> GenerateAsync(
            string prompt,
            AttachedContextItem? context,
            Action<string> reportChunk,
            CancellationToken cancellationToken)
        {
            GenerateCalls++;
            GeneratedPrompt = prompt;
            GeneratedContext = context;
            LastReportChunk = reportChunk;
            GenerationStarted.TrySetResult();
            foreach (var chunk in Chunks)
            {
                reportChunk(chunk);
            }

            if (Failure is not null)
            {
                throw Failure;
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

        public void ReleaseCapture() => _captureReleased.TrySetResult();

        public void ReleaseScreenCapture() => _screenCaptureReleased.TrySetResult();

        public Task CopyTextAsync(string text, CancellationToken cancellationToken)
        {
            CopiedText = text;
            return Task.CompletedTask;
        }

        public Task OpenInAedaAsync()
        {
            OpenCalls++;
            return Task.CompletedTask;
        }
    }
}
