using PersonalAI.Core.Chat;
using PersonalAI.Core.Editor;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Infrastructure.Ipc;

namespace PersonalAI.Tests.Editor;

public sealed class EditorCodeChatResponderTests
{
    [Fact]
    public async Task Handler_ReturnsAssistantResponseInsteadOfPlaceholderOk()
    {
        var provider = new FakeChatProvider([new ChatChunk("real explanation", true)]);
        var responder = CreateResponder(provider);
        var handler = new EditorContextMessageHandler(
            _ => { },
            () => { },
            responder.RespondAsync);

        var result = await handler.HandleAsync(CreateEnvelope());

        Assert.True(result.Ok);
        Assert.Equal("real explanation", result.Message);
        Assert.NotEqual("ok", result.Message);
    }

    [Fact]
    public async Task Handler_CanWaitLongerThanOldExtensionTimeout()
    {
        var handler = new EditorContextMessageHandler(
            _ => { },
            () => { },
            async (_, cancellationToken) =>
            {
                await Task.Delay(2600, cancellationToken);
                return EditorContextHandlerResult.Success("slow response");
            });

        var result = await handler.HandleAsync(CreateEnvelope());

        Assert.True(result.Ok);
        Assert.Equal("slow response", result.Message);
    }

    [Fact]
    public async Task ExplainCommand_DelegatesToChatProviderWithoutTools()
    {
        var provider = new FakeChatProvider([new ChatChunk("explained", true)]);
        var responder = CreateResponder(provider);

        var result = await responder.RespondAsync(CreateEnvelope());

        Assert.True(result.Ok);
        var request = Assert.Single(provider.Requests);
        Assert.Empty(request.Tools);
        Assert.Equal("qwen2.5-coder:7b", request.Model);
        Assert.Contains("Explain what this selected code does", PromptText(request));
        Assert.Contains("public void Run()", PromptText(request));
    }

    [Fact]
    public async Task AskCommand_IncludesUserQuestionAndSelectedCode()
    {
        var provider = new FakeChatProvider([new ChatChunk("answer", true)]);
        var responder = CreateResponder(provider);

        await responder.RespondAsync(CreateEnvelope(
            EditorContextCommands.AskAboutSelection,
            "Why is this async?"));

        var prompt = PromptText(Assert.Single(provider.Requests));
        Assert.Contains("Question: Why is this async?", prompt);
        Assert.Contains("public void Run()", prompt);
    }

    [Fact]
    public async Task FindProblemsCommand_UsesReadOnlyReviewPrompt()
    {
        var provider = new FakeChatProvider([new ChatChunk("review", true)]);
        var responder = CreateResponder(provider);

        await responder.RespondAsync(CreateEnvelope(
            EditorContextCommands.FindProblemsInSelection));

        var prompt = PromptText(Assert.Single(provider.Requests));
        Assert.Contains("Review this selected code for likely bugs", prompt);
        Assert.Contains("Do not modify files", prompt);
    }

    [Fact]
    public async Task EmptySelection_ReturnsControlledMessageWithoutProviderCall()
    {
        var provider = new FakeChatProvider([new ChatChunk("unused", true)]);
        var responder = CreateResponder(provider);

        var result = await responder.RespondAsync(CreateEnvelope(selectedText: ""));

        Assert.False(result.Ok);
        Assert.Equal("Select code in VS Code before using this command.", result.Message);
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task OversizedSelection_IsBoundedAndMarked()
    {
        var provider = new FakeChatProvider([new ChatChunk("bounded", true)]);
        var responder = CreateResponder(provider);
        var oversized = new string('x', EditorCodeChatResponder.MaxPromptSelectedTextCharacters + 500);

        await responder.RespondAsync(CreateEnvelope(selectedText: oversized));

        var prompt = PromptText(Assert.Single(provider.Requests));
        Assert.Contains("Selected code was truncated", prompt);
        Assert.True(prompt.Length < oversized.Length + 2_000);
    }

    [Fact]
    public async Task ProviderFailure_ReturnsSafeError()
    {
        var provider = new FakeChatProvider([]);
        provider.ExceptionToThrow = new InvalidOperationException(
            "raw secret C:\\users\\name\\token.txt");
        var responder = CreateResponder(provider);

        var result = await responder.RespondAsync(CreateEnvelope());

        Assert.False(result.Ok);
        Assert.Equal(
            "PersonalAI could not reach an available local chat provider.",
            result.Message);
        Assert.DoesNotContain("token", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancellation_ReturnsControlledCancelledState()
    {
        var provider = new FakeChatProvider([]);
        provider.CancelDuringStream = true;
        var responder = CreateResponder(provider);

        var result = await responder.RespondAsync(CreateEnvelope());

        Assert.False(result.Ok);
        Assert.Equal("Request cancelled.", result.Message);
    }

    [Fact]
    public async Task Prompt_DoesNotIncludeAbsolutePaths()
    {
        var provider = new FakeChatProvider([new ChatChunk("ok", true)]);
        var responder = CreateResponder(provider);

        await responder.RespondAsync(CreateEnvelope());

        var prompt = PromptText(Assert.Single(provider.Requests));
        Assert.DoesNotContain(@"C:\repo", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Relative path: src/Program.cs", prompt);
        Assert.Contains("File name: Program.cs", prompt);
    }

    [Fact]
    public void PipeResponseContract_RemainsPascalCaseCompatible()
    {
        var response = System.Text.Json.JsonSerializer.Serialize(
            new EditorContextHandlerResult(true, "message"));

        Assert.Contains("\"Ok\":true", response);
        Assert.Contains("\"Message\":\"message\"", response);
    }

    private static EditorCodeChatResponder CreateResponder(
        FakeChatProvider provider,
        IReadOnlyList<string>? installedModels = null)
    {
        var settings = ApplicationSettings.CreateDefault() with
        {
            Models = new ModelSettings(
            [
                new ModelRoutingAssignment(
                    ModelRoutingCategory.Coding,
                    "configured-coder"),
                new ModelRoutingAssignment(
                    ModelRoutingCategory.General,
                    "gemma4")
            ])
        };
        return new EditorCodeChatResponder(
            new ChatSessionService(provider),
            new FakeSettingsService(settings),
            _ => Task.FromResult(installedModels ?? ["qwen2.5-coder:7b"]));
    }

    private static EditorContextEnvelope CreateEnvelope(
        string command = EditorContextCommands.ExplainSelection,
        string? userPrompt = "Explain the selected code.",
        string? selectedText = "public void Run() {}") =>
        new(
            1,
            "request-1",
            ContextSource.Vscode,
            command,
            userPrompt,
            new EditorContext(
                selectedText,
                @"C:\repo\src\Program.cs",
                "src/Program.cs",
                "Program.cs",
                "csharp",
                new TextRange(0, 0, 0, 20),
                "repo",
                @"C:\repo",
                1,
                IsDirty: false,
                [],
                DateTimeOffset.UtcNow));

    private static string PromptText(ChatRequest request) =>
        string.Join('\n', request.Messages.Select(message => message.Content));

    private sealed class FakeChatProvider(IReadOnlyList<ChatChunk> chunks)
        : IChatProvider
    {
        public string ProviderName => "Fake";

        public List<ChatRequest> Requests { get; } = [];

        public Exception? ExceptionToThrow { get; set; }

        public bool CancelDuringStream { get; set; }

        public async IAsyncEnumerable<ChatChunk> StreamAsync(
            ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            if (CancelDuringStream)
            {
                throw new OperationCanceledException();
            }

            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return chunk;
            }
        }
    }

    private sealed class FakeSettingsService(ApplicationSettings settings)
        : IApplicationSettingsService
    {
        public ApplicationSettings Current { get; private set; } = settings;

        public string SettingsPath => "test-settings.json";

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveAsync(
            ApplicationSettings settings,
            CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
