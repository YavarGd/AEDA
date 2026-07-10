using PersonalAI.Core.Chat;
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
    public void StartsAccordingToEnabledSetting(
        bool enabled,
        AssistPillState expected)
    {
        var viewModel = CreateViewModel(new FakeHost(), enabled);

        Assert.Equal(expected, viewModel.State);
    }

    [Fact]
    public async Task OpenPrompt_UsesContextualModeForSafeBoundedContext()
    {
        var host = new FakeHost { Context = Context(new string('p', 500)) };
        var viewModel = CreateViewModel(host);

        await viewModel.OpenPromptAsync();

        Assert.Equal(AssistPillState.ContextPrompt, viewModel.State);
        Assert.True(viewModel.HasContext);
        Assert.True(viewModel.ContextPreview.Length <= 180);
        Assert.Equal(1, host.CaptureCalls);
    }

    [Fact]
    public async Task OpenPrompt_UsesPrivacySpotlightWhenContextIsBlocked()
    {
        var viewModel = CreateViewModel(new FakeHost { Context = null });

        await viewModel.OpenPromptAsync();

        Assert.Equal(AssistPillState.SpotlightPrompt, viewModel.State);
        Assert.False(viewModel.HasContext);
        Assert.Contains("Privacy", viewModel.StatusText);
    }

    [Fact]
    public async Task RepeatedOpenWhileCapturing_IsIgnoredAndDoesNotDuplicateWork()
    {
        var host = new FakeHost { WaitForCapture = true };
        var viewModel = CreateViewModel(host);

        var firstOpen = viewModel.OpenPromptAsync();
        await host.CaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var repeatedOpen = await viewModel.OpenPromptAsync();

        Assert.False(repeatedOpen);
        Assert.Equal(1, host.CaptureCalls);
        host.ReleaseCapture();
        Assert.True(await firstOpen);
        Assert.Equal(AssistPillState.SpotlightPrompt, viewModel.State);
    }

    [Fact]
    public async Task OpenGuard_ReleasesAfterCaptureFailure()
    {
        var host = new FakeHost
        {
            CaptureFailure = new InvalidOperationException("capture failed")
        };
        var viewModel = CreateViewModel(host);

        Assert.True(await viewModel.OpenPromptAsync());
        viewModel.Collapse();
        Assert.True(await viewModel.OpenPromptAsync());

        Assert.Equal(2, host.CaptureCalls);
        Assert.Equal(AssistPillState.SpotlightPrompt, viewModel.State);
    }

    [Fact]
    public async Task CompletedAndFailedStates_CanCollapseAndReopen()
    {
        var host = new FakeHost();
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "First";
        await viewModel.SubmitAsync();

        viewModel.Collapse();
        Assert.Equal(AssistPillState.IdlePill, viewModel.State);
        Assert.True(await viewModel.OpenPromptAsync());

        Assert.Equal(AssistPillState.SpotlightPrompt, viewModel.State);
        Assert.Equal(2, host.CaptureCalls);
    }

    [Fact]
    public async Task ContextCanBeRemovedBeforeSend()
    {
        var host = new FakeHost { Context = Context("Safe preview") };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();

        viewModel.RemoveContext();
        viewModel.Prompt = "Explain this";
        await viewModel.SubmitAsync();

        Assert.Equal(AssistPillState.Completed, viewModel.State);
        Assert.Null(host.GeneratedContext);
    }

    [Fact]
    public async Task EmptyPromptCannotSend()
    {
        var host = new FakeHost();
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();

        viewModel.Prompt = "   ";
        await viewModel.SubmitAsync();

        Assert.False(viewModel.CanSubmit);
        Assert.Equal(0, host.GenerateCalls);
    }

    [Fact]
    public async Task StreamingEnablesStopAndCancellationIsControlled()
    {
        var host = new FakeHost { WaitForCancellation = true };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "Long request";

        var submit = viewModel.SubmitAsync();
        await host.GenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(AssistPillState.StreamingResponse, viewModel.State);
        Assert.True(viewModel.CanCancel);
        viewModel.Cancel();
        await submit;

        Assert.Equal(AssistPillState.Cancelled, viewModel.State);
        Assert.Equal("Cancelled", viewModel.StatusText);
    }

    [Fact]
    public async Task FailureDoesNotExposeRawException()
    {
        var host = new FakeHost
        {
            Failure = new InvalidOperationException("raw provider secret")
        };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "Fail safely";

        await viewModel.SubmitAsync();

        Assert.Equal(AssistPillState.Failed, viewModel.State);
        Assert.DoesNotContain("raw provider secret", viewModel.StatusText);
    }

    [Fact]
    public async Task ResponsePreviewIsBoundedAndHidesReasoning()
    {
        var host = new FakeHost
        {
            Responses = ["<think>private reasoning</think>" + new string('a', 500)]
        };
        var viewModel = CreateViewModel(host, previewCharacters: 200);
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "Summarize";

        await viewModel.SubmitAsync();
        await viewModel.CopyResponseAsync();

        Assert.Equal(AssistPillState.Completed, viewModel.State);
        Assert.True(viewModel.Response.Length <= 200);
        Assert.DoesNotContain("private reasoning", viewModel.Response);
        Assert.DoesNotContain("private reasoning", host.CopiedText);
        Assert.True(host.CopiedText.Length > viewModel.Response.Length);
    }

    [Fact]
    public async Task OpenInAedaPreservesExplicitPromptAndContext()
    {
        var context = Context("Safe selection");
        var host = new FakeHost { Context = context };
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "Continue this work";

        await viewModel.SubmitAsync();
        viewModel.OpenInAeda();

        Assert.Equal("Continue this work", host.GeneratedPrompt);
        Assert.Same(context, host.GeneratedContext);
        Assert.Equal(1, host.OpenInAedaCalls);
        Assert.Equal(AssistPillState.Hidden, viewModel.State);
    }

    [Fact]
    public async Task GenerationNeverTriggersAutomaticModuleHandoff()
    {
        var host = new FakeHost();
        var viewModel = CreateViewModel(host);
        await viewModel.OpenPromptAsync();
        viewModel.Prompt = "Verify and refactor this code";

        await viewModel.SubmitAsync();

        Assert.Equal(0, host.OpenInAedaCalls);
    }

    [Fact]
    public async Task ContextPresentationDoesNotExposePayloadPathOrScreenshot()
    {
        var host = new FakeHost
        {
            Context = Context(
                "Safe preview",
                providerPayload: "password=hunter2",
                metadata: new Dictionary<string, string>
                {
                    ["executablePath"] = @"C:\secret\app.exe",
                    ["screenshot"] = "base64-secret"
                })
        };
        var viewModel = CreateViewModel(host);

        await viewModel.OpenPromptAsync();

        var visible = viewModel.ContextLabel + viewModel.ContextPreview;
        Assert.DoesNotContain("hunter2", visible);
        Assert.DoesNotContain("secret\\app", visible);
        Assert.DoesNotContain("base64", visible);
    }

    [Fact]
    public void IdleDoesNotCaptureContextAutomatically()
    {
        var host = new FakeHost();
        var viewModel = CreateViewModel(host);

        viewModel.ShowIdle();

        Assert.Equal(0, host.CaptureCalls);
    }

    private static AssistPillViewModel CreateViewModel(
        FakeHost host,
        bool enabled = true,
        int previewCharacters = 1_200) =>
        new(host, new AssistPillSettings(enabled, previewCharacters));

    private static AttachedContextItem Context(
        string preview,
        string providerPayload = "safe provider payload",
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            Guid.NewGuid(),
            AttachedContextType.ApplicationWindow,
            "Editor",
            "Document",
            preview,
            providerPayload,
            Images: [],
            ThumbnailDataUri: null,
            metadata ?? new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid().ToString("N"));

    private sealed class FakeHost : IAssistPillHost
    {
        private readonly TaskCompletionSource<ChatStatus> _cancelled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsGenerating { get; set; }

        public AttachedContextItem? Context { get; init; }

        public IReadOnlyList<string> Responses { get; init; } = ["Done"];

        public Exception? Failure { get; init; }

        public Exception? CaptureFailure { get; init; }

        public bool WaitForCancellation { get; init; }

        public bool WaitForCapture { get; init; }

        public int CaptureCalls { get; private set; }

        public int GenerateCalls { get; private set; }

        public int OpenInAedaCalls { get; private set; }

        public string? GeneratedPrompt { get; private set; }

        public AttachedContextItem? GeneratedContext { get; private set; }

        public string CopiedText { get; private set; } = string.Empty;

        public TaskCompletionSource GenerationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CaptureStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource CaptureReleased { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AttachedContextItem?> CaptureContextAsync(
            CancellationToken cancellationToken)
        {
            CaptureCalls++;
            CaptureStarted.TrySetResult();
            if (WaitForCapture)
            {
                await CaptureReleased.Task.WaitAsync(cancellationToken);
            }

            if (CaptureFailure is not null)
            {
                throw CaptureFailure;
            }

            return Context;
        }

        public void ReleaseCapture() => CaptureReleased.TrySetResult();

        public async Task<ChatStatus> GenerateAsync(
            string prompt,
            AttachedContextItem? context,
            Action<string> reportResponse,
            CancellationToken cancellationToken)
        {
            GenerateCalls++;
            GeneratedPrompt = prompt;
            GeneratedContext = context;
            GenerationStarted.TrySetResult();

            if (Failure is not null)
            {
                throw Failure;
            }

            foreach (var response in Responses)
            {
                reportResponse(response);
            }

            return WaitForCancellation
                ? await _cancelled.Task
                : ChatStatus.Completed;
        }

        public void CancelGeneration()
        {
            _cancelled.TrySetResult(ChatStatus.Cancelled);
        }

        public Task CopyTextAsync(string text, CancellationToken cancellationToken)
        {
            CopiedText = text;
            return Task.CompletedTask;
        }

        public void OpenInAeda()
        {
            OpenInAedaCalls++;
        }
    }
}
