using System.Text;
using System.Text.Json;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Providers;
using PersonalAI.Core.Settings;

namespace PersonalAI.Infrastructure.Coding;

public sealed class CodeProposalDraftService(
    IModelRoutingPolicy routingPolicy,
    IContextPrivacyFilter privacyFilter,
    IReadOnlyDictionary<ProviderId, IChatProvider> chatProviders,
    Func<ProviderRoutingSettings> providerRoutingSettingsProvider,
    TimeSpan? modelGenerationTimeout = null) : ICodeProposalDraftService
{
    private const int MaxRequestCharacters = 4_000;
    private const int MaxTitleCharacters = 120;
    private const int MaxSummaryCharacters = 700;
    private const int MaxModelOutputCharacters = 240_000;
    private const int MaxExtractedJsonCharacters = 220_000;
    private const int MaxFileEdits = 12;
    private const int MaxProposedFileCharacters = 160_000;
    private static readonly TimeSpan DefaultModelGenerationTimeout = TimeSpan.FromSeconds(180);

    public async Task<CodeProposalDraft> CreateDraftAsync(
        CodeProposalDraftRequest request,
        IProgress<AedaCodeProposalCreationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ChangeRequest.UserRequest))
        {
            throw Failure(AedaCodeProposalCreationFailureReason.RequestEmpty);
        }

        if (request.ChangeRequest.UserRequest.Length > MaxRequestCharacters)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.RequestTooLong);
        }

        if (request.Context.Files.Count == 0)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.NoSafeContext);
        }

        var settings = providerRoutingSettingsProvider();
        var decision = await routingPolicy.SelectAsync(
            new ModelRoutingPolicyRequest(
                RequiredCapability: ModelCapability.Code,
                LocalOnlyMode: settings.LocalOnlyMode,
                AllowRemoteChat: settings.AllowRemoteChat,
                AllowRemoteEmbeddings: settings.AllowRemoteEmbeddings,
                AllowRemoteWorkspaceContext: settings.AllowRemoteWithWorkspaceContext,
                AllowRemoteMemoryContext: settings.AllowRemoteWithMemoryContext,
                AllowRemoteScreenshots: settings.AllowRemoteWithScreenshots,
                AllowRemoteClipboardOrAppContext: settings.AllowRemoteWithClipboardOrAppContext,
                IncludesWorkspaceContent: true,
                IncludesMemoryContext: false,
                IncludesScreenshot: false,
                IncludesClipboardOrAppContext: false,
                Sensitivity: RoutingContextSensitivity.Sensitive,
                ProviderOverride: string.IsNullOrWhiteSpace(settings.SelectedChatProvider)
                    ? null
                    : new ProviderId(settings.SelectedChatProvider)),
            cancellationToken).ConfigureAwait(false);

        if (!decision.IsAllowed || decision.Provider is null || decision.Model is null)
        {
            throw Failure(IsPolicyRejection(decision.SafeReasonCode)
                ? AedaCodeProposalCreationFailureReason.ProviderRejectedByPolicy
                : AedaCodeProposalCreationFailureReason.ProviderUnavailable);
        }

        if (!chatProviders.TryGetValue(decision.Provider.Id, out var provider))
        {
            throw Failure(AedaCodeProposalCreationFailureReason.ProviderUnavailable);
        }

        var messages = BuildMessages(request);
        var filtered = await privacyFilter.FilterAsync(
            new ContextPrivacyFilterRequest(
                decision.Provider,
                messages,
                AllowWorkspaceContext: !decision.MustStripWorkspaceContext,
                AllowMemoryContext: false,
                AllowScreenshots: false,
                AllowClipboardOrAppContext: false),
            cancellationToken).ConfigureAwait(false);

        if (filtered.Messages.All(message => string.IsNullOrWhiteSpace(message.Content)))
        {
            throw Failure(AedaCodeProposalCreationFailureReason.ProviderRejectedByPolicy);
        }

        progress?.Report(new AedaCodeProposalCreationProgress(
            AedaCodeProposalCreationPhase.CallingCodingModel,
            SafeContextFileCount: request.Context.Files.Count,
            SafeProviderLabel: decision.Provider.DisplayName));
        var chatRequest = new ChatRequest(decision.Model.ModelId.Value, filtered.Messages);
        var output = await CollectProviderOutputAsync(provider, chatRequest, cancellationToken)
            .ConfigureAwait(false);
        CodeProposalDraft draft;
        try
        {
            progress?.Report(new AedaCodeProposalCreationProgress(
                AedaCodeProposalCreationPhase.ParsingModelDraft,
                SafeContextFileCount: request.Context.Files.Count));
            draft = ValidateDraft(ParseDraft(output, request), request);
        }
        catch (AedaCodeProposalCreationException exception)
            when (CanRetry(exception.Failure.Reason))
        {
            var schemaIssueCode = exception.Failure.SchemaIssueCode ?? exception.Failure.SafeCode;
            progress?.Report(new AedaCodeProposalCreationProgress(
                AedaCodeProposalCreationPhase.RetryingStructuredDraft,
                SafeContextFileCount: request.Context.Files.Count,
                RetryAttempted: true,
                SchemaIssueCode: schemaIssueCode));
            var retryMessages = BuildRetryMessages(request, output, schemaIssueCode);
            var retryFiltered = await privacyFilter.FilterAsync(
                new ContextPrivacyFilterRequest(
                    decision.Provider,
                    retryMessages,
                    AllowWorkspaceContext: !decision.MustStripWorkspaceContext,
                    AllowMemoryContext: false,
                    AllowScreenshots: false,
                    AllowClipboardOrAppContext: false),
                cancellationToken).ConfigureAwait(false);
            if (retryFiltered.Messages.All(message => string.IsNullOrWhiteSpace(message.Content)))
            {
                throw Failure(AedaCodeProposalCreationFailureReason.ProviderRejectedByPolicy);
            }

            var retryOutput = await CollectProviderOutputAsync(
                provider,
                new ChatRequest(decision.Model.ModelId.Value, retryFiltered.Messages),
                cancellationToken).ConfigureAwait(false);
            try
            {
                progress?.Report(new AedaCodeProposalCreationProgress(
                    AedaCodeProposalCreationPhase.ParsingModelDraft,
                    SafeContextFileCount: request.Context.Files.Count,
                    RetryAttempted: true,
                    SchemaIssueCode: schemaIssueCode));
                draft = ValidateDraft(ParseDraft(retryOutput, request), request);
            }
            catch (AedaCodeProposalCreationException retryException)
                when (CanRetry(retryException.Failure.Reason))
            {
                throw WithRetryAttempted(retryException);
            }
        }

        return draft with
        {
            SafeNotices = draft.SafeNotices
                .Concat(filtered.RemovedSafeSummaries)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static IReadOnlyList<ChatMessage> BuildMessages(
        CodeProposalDraftRequest request)
    {
        if (request.SelectedTargetSnippet is not null)
        {
            return BuildSelectedTargetMessages(request);
        }

        var allowedPaths = request.Context.Files
            .Select(file => file.RelativePath)
            .ToArray();
        var examplePath = allowedPaths.First();
        var serializedExamplePath = JsonSerializer.Serialize(examplePath);
        var builder = new StringBuilder();
        builder.AppendLine("[workspace] Bounded workspace context for proposal creation.");
        builder.AppendLine("Return only one JSON object.");
        builder.AppendLine("No markdown. No prose. No code fences. No extra keys.");
        if (request.SelectedTargetSnippet is not null)
        {
            builder.AppendLine("Use this exact schema for the selected snippet edit:");
            builder.AppendLine($$"""{"title":"string","summary":"string","changes":[{"relativePath":{{serializedExamplePath}},"replacementText":"entire selected snippet with only the requested edit"}],"safeNotices":[]}""");
        }
        else
        {
            builder.AppendLine("Use this exact schema for small edits:");
            builder.AppendLine($$"""{"title":"string","summary":"string","changes":[{"relativePath":{{serializedExamplePath}},"originalText":"exact text copied from the current file","replacementText":"same text with only the requested edit"}],"safeNotices":[]}""");
            builder.AppendLine("Full-file fallback schema, only when explicitly asked and safe:");
            builder.AppendLine($$"""{"title":"string","summary":"string","changes":[{"relativePath":{{serializedExamplePath}},"proposedContent":"complete final file content"}],"safeNotices":[]}""");
        }
        builder.AppendLine(request.SelectedTargetSnippet is null
            ? "Required fields: title, summary, changes, relativePath, then either originalText+replacementText or proposedContent."
            : "Required fields: title, summary, changes, relativePath, replacementText.");
        builder.AppendLine("Rules:");
        builder.AppendLine("- changes must be a non-empty array.");
        builder.AppendLine("- Target only one of the allowed target files below.");
        builder.AppendLine("- If the user says selected file, that means one of the allowed target files below; selected file is not a relativePath.");
        builder.AppendLine("- Use exactly one allowed target file as relativePath.");
        builder.AppendLine("- Do not shorten file names.");
        builder.AppendLine("- Do not use only the basename unless it appears exactly that way in the allowed list.");
        builder.AppendLine("- Do not invent file paths.");
        builder.AppendLine("- Do not target .csproj files unless a .csproj path is explicitly listed as an allowed target file.");
        builder.AppendLine("- Do not invent helper methods; modify an existing helper from the context.");
        builder.AppendLine("- Do not delete files.");
        builder.AppendLine("- Keep changes minimal.");
        if (request.SelectedTargetSnippet is null)
        {
            builder.AppendLine("- For existing file modifications, prefer originalText + replacementText.");
            builder.AppendLine("- originalText must be copied exactly from the file.");
            builder.AppendLine("- replacementText must preserve the same code with only the requested tiny edit.");
            builder.AppendLine("- Do not output a partial snippet as proposedContent.");
            builder.AppendLine("- proposedContent must be the complete proposed content for the same existing file.");
        }
        else
        {
            builder.AppendLine("- The user selected the exact method/snippet.");
            builder.AppendLine("- You must edit the selected target snippet only.");
            builder.AppendLine("- Do not return originalText.");
            builder.AppendLine("- Return only replacementText for that same snippet.");
            builder.AppendLine("- replacementText must contain the entire selected snippet plus only the requested XML documentation comment.");
            if (RequiresXmlDocComment(request.ChangeRequest.UserRequest))
            {
                builder.AppendLine("- Use C# XML documentation comment lines with ///.");
                builder.AppendLine("- Do not use // <summary>.");
                builder.AppendLine("- XML doc lines must use the same indentation as the selected method.");
            }

            builder.AppendLine("- Do not change the method body.");
            builder.AppendLine("- Do not choose another method.");
            builder.AppendLine("- Do not output proposedContent.");
            builder.AppendLine("- Do not output full file content.");
        }
        builder.AppendLine("- The proposal is not applied.");
        builder.AppendLine();
        if (allowedPaths.Length == 1)
        {
            builder.AppendLine($"Allowed target file: {allowedPaths[0]}");
        }
        else
        {
            builder.AppendLine("Allowed target files:");
        }

        foreach (var path in allowedPaths)
        {
            builder.AppendLine($"- {path}");
        }

        AppendTargetSnippetBlock(builder, request);
        builder.AppendLine();
        builder.AppendLine("Valid example:");
        builder.AppendLine(request.SelectedTargetSnippet is null
            ? $$"""{"title":"Add XML docs","summary":"Adds a documentation comment without behavior changes.","changes":[{"relativePath":{{serializedExamplePath}},"originalText":"private void Helper()\n{\n    DoWork();\n}","replacementText":"/// <summary>\n/// Performs the helper work.\n/// </summary>\nprivate void Helper()\n{\n    DoWork();\n}"}],"safeNotices":[]}"""
            : $$"""{"title":"Add XML docs","summary":"Adds a documentation comment without behavior changes.","changes":[{"relativePath":{{serializedExamplePath}},"replacementText":"/// <summary>\n/// Performs the helper work.\n/// </summary>\nprivate void Helper()\n{\n    DoWork();\n}"}],"safeNotices":[]}""");
        builder.AppendLine();
        builder.AppendLine("User request:");
        builder.AppendLine(Bound(request.ChangeRequest.UserRequest, MaxRequestCharacters));
        if (!string.IsNullOrWhiteSpace(request.OptionalTitle))
        {
            builder.AppendLine();
            builder.AppendLine("Optional task title:");
            builder.AppendLine(Bound(request.OptionalTitle!, MaxTitleCharacters));
        }

        builder.AppendLine();
        builder.AppendLine("Context files:");
        foreach (var file in request.Context.Files)
        {
            builder.AppendLine($"--- file: {file.RelativePath}");
            builder.AppendLine(file.Content);
            builder.AppendLine($"--- end file: {file.RelativePath}");
        }

        return
        [
            new ChatMessage(
                ChatRole.System,
                "You create safe patch proposals. Return exactly one strict JSON object and nothing else."),
            new ChatMessage(ChatRole.User, builder.ToString())
        ];
    }

    private static IReadOnlyList<ChatMessage> BuildRetryMessages(
        CodeProposalDraftRequest request,
        string previousOutput,
        string? schemaIssueCode)
    {
        if (request.SelectedTargetSnippet is not null)
        {
            return BuildSelectedTargetRetryMessages(request, previousOutput, schemaIssueCode);
        }

        var allowedPaths = request.Context.Files
            .Select(file => file.RelativePath)
            .ToArray();
        var allowedPathText = string.Join(", ", allowedPaths);
        var examplePath = JsonSerializer.Serialize(allowedPaths.First());
        return
        [
            new ChatMessage(
                ChatRole.System,
                "Repair the previous proposal draft into exactly one valid JSON object. Do not add new changes."),
            new ChatMessage(
                ChatRole.User,
                string.Join(
                    Environment.NewLine,
                    [
                        "[workspace] Proposal draft JSON repair request.",
                        "Return only one JSON object. No markdown. No prose. No code fences.",
                        request.SelectedTargetSnippet is null
                            ? $"Small edit schema: {{\"title\":\"string\",\"summary\":\"string\",\"changes\":[{{\"relativePath\":{examplePath},\"originalText\":\"exact current text\",\"replacementText\":\"same text with the requested edit\"}}],\"safeNotices\":[]}}"
                            : $"Selected snippet schema: {{\"title\":\"string\",\"summary\":\"string\",\"changes\":[{{\"relativePath\":{examplePath},\"replacementText\":\"entire selected snippet with only the requested edit\"}}],\"safeNotices\":[]}}",
                        request.SelectedTargetSnippet is null
                            ? $"Full-file fallback schema only when safe: {{\"title\":\"string\",\"summary\":\"string\",\"changes\":[{{\"relativePath\":{examplePath},\"proposedContent\":\"complete final file content\"}}],\"safeNotices\":[]}}"
                            : "Do not return originalText or proposedContent when a target snippet is selected.",
                        $"Safe schema issue code: {schemaIssueCode ?? "unknown_schema_issue"}",
                        request.SelectedTargetSnippet is not null
                            ? SelectedTargetRetryInstruction(request)
                            : schemaIssueCode == "target_text_not_found"
                            ? "Correction required: your originalText did not match the current file exactly. Choose a smaller exact contiguous snippet copied from the selected file. originalText must match exactly once."
                            : "Correction required: make the JSON match one supported schema exactly.",
                        "Target only one of these allowed target files. If the user said selected file, use one allowed target file.",
                        "Do not target .csproj files unless a .csproj path is explicitly listed as an allowed target file.",
                        "Do not invent helper methods; modify an existing helper from the context.",
                        "Use exactly one of these allowed relativePath values. Do not shorten file names.",
                        request.SelectedTargetSnippet is null
                            ? "Do not output a partial snippet as proposedContent."
                            : "Do not output full file content.",
                        allowedPaths.Length == 1
                            ? $"Allowed target file: {allowedPaths[0]}"
                            : $"Allowed target files: {allowedPathText}",
                        BuildTargetSnippetBlock(request),
                        "If the previous output had explanations, remove them.",
                        "If a field is missing, infer only from the previous output and original request.",
                        "Original request:",
                        Bound(request.ChangeRequest.UserRequest, MaxRequestCharacters),
                        "Previous output to reformat:",
                        Bound(previousOutput, 20_000)
                    ]))
        ];
    }

    private static IReadOnlyList<ChatMessage> BuildSelectedTargetMessages(
        CodeProposalDraftRequest request)
    {
        var snippet = request.SelectedTargetSnippet!;
        var pathJson = JsonSerializer.Serialize(snippet.RelativePath);
        var builder = new StringBuilder();
        builder.AppendLine("Selected target snippet mode.");
        builder.AppendLine("Output exactly one JSON object.");
        builder.AppendLine("Do not output markdown. Do not output code fences. Do not output explanations. Do not output unified diff.");
        builder.AppendLine("Do not output full file content. Do not include originalText. Do not include proposedContent.");
        builder.AppendLine("Do not return originalText.");
        builder.AppendLine("Use exactly this schema:");
        if (RequiresXmlDocComment(request.ChangeRequest.UserRequest))
        {
            builder.AppendLine("""{"title":"string","summary":"string","documentation":{"summary":"string"}}""");
        }
        else
        {
            builder.AppendLine($$"""{"title":"string","summary":"string","changes":[{"relativePath":{{pathJson}},"replacementText":"string"}]}""");
        }

        builder.AppendLine();
        builder.AppendLine($"Allowed relativePath: {snippet.RelativePath}");
        if (RequiresXmlDocComment(request.ChangeRequest.UserRequest))
        {
            builder.AppendLine("Only write documentation.summary text.");
            builder.AppendLine("Do not include code. Do not include the method. Do not include ///.");
            builder.AppendLine("The backend will add /// XML doc lines and preserve the selected method exactly.");
        }
        else
        {
            builder.AppendLine("replacementText must contain the selected snippet with only the requested edit.");
            builder.AppendLine("Preserve the exact selected method indentation, signature, and body.");
        }

        builder.AppendLine();
        builder.AppendLine("Example selected snippet:");
        builder.AppendLine("```csharp");
        builder.AppendLine("    private bool CanRun()");
        builder.AppendLine("    {");
        builder.AppendLine("        return !IsBusy;");
        builder.AppendLine("    }");
        builder.AppendLine("```");
        builder.AppendLine("Example valid JSON:");
        builder.AppendLine(RequiresXmlDocComment(request.ChangeRequest.UserRequest)
            ? """{"title":"Add XML documentation comment","summary":"Adds XML documentation to the selected method without changing behavior.","documentation":{"summary":"Determines whether the operation can run."}}"""
            : $$"""{"title":"Update selected method","summary":"Updates the selected method without changing unrelated code.","changes":[{"relativePath":{{pathJson}},"replacementText":"    private bool CanRun()\n    {\n        return CanRunNow();\n    }"}]}""");
        builder.AppendLine("Your real output must use the real selected snippet below, not the example method.");
        builder.AppendLine();
        builder.AppendLine("User request:");
        builder.AppendLine(Bound(request.ChangeRequest.UserRequest, MaxRequestCharacters));
        builder.AppendLine();
        builder.AppendLine("Selected snippet:");
        builder.AppendLine("```csharp");
        builder.AppendLine(snippet.Text);
        builder.AppendLine("```");

        return
        [
            new ChatMessage(
                ChatRole.System,
                "You create safe patch proposal JSON. Return exactly one strict JSON object and nothing else."),
            new ChatMessage(ChatRole.User, builder.ToString())
        ];
    }

    private static IReadOnlyList<ChatMessage> BuildSelectedTargetRetryMessages(
        CodeProposalDraftRequest request,
        string previousOutput,
        string? schemaIssueCode)
    {
        var snippet = request.SelectedTargetSnippet!;
        var pathJson = JsonSerializer.Serialize(snippet.RelativePath);
        return
        [
            new ChatMessage(
                ChatRole.System,
                "Repair the selected-snippet proposal into exactly one valid JSON object. Do not add explanations."),
            new ChatMessage(
                ChatRole.User,
                string.Join(
                    Environment.NewLine,
                    [
                        schemaIssueCode == "invalid_model_json"
                            ? "Your previous response was not valid JSON."
                            : $"Your previous response was rejected. Safe schema issue code: {schemaIssueCode ?? "unknown_schema_issue"}.",
                        "Output exactly one JSON object.",
                        "No markdown. No code fences. No explanations. No unified diff.",
                        "Do not include originalText. Do not include proposedContent. Do not output full file content.",
                        RequiresXmlDocComment(request.ChangeRequest.UserRequest)
                            ? "Schema: {\"title\":\"string\",\"summary\":\"string\",\"documentation\":{\"summary\":\"string\"}}"
                            : $"Schema: {{\"title\":\"string\",\"summary\":\"string\",\"changes\":[{{\"relativePath\":{pathJson},\"replacementText\":\"string\"}}]}}",
                        $"Allowed relativePath: {snippet.RelativePath}",
                        RequiresXmlDocComment(request.ChangeRequest.UserRequest)
                            ? "Only write documentation.summary text. Do not include code, the method, or ///."
                            : "replacementText must contain the selected snippet with only the requested edit.",
                        RequiresXmlDocComment(request.ChangeRequest.UserRequest)
                            ? "The backend will preserve the selected method exactly."
                            : "Preserve the exact selected method indentation, signature, and body.",
                        "Selected snippet:",
                        "```csharp",
                        snippet.Text,
                        "```",
                        "Previous response:",
                        Bound(previousOutput, 12_000)
                    ]))
        ];
    }

    private static void AppendTargetSnippetBlock(
        StringBuilder builder,
        CodeProposalDraftRequest request)
    {
        var block = BuildTargetSnippetBlock(request);
        if (!string.IsNullOrWhiteSpace(block))
        {
            builder.AppendLine();
            builder.AppendLine(block);
        }
    }

    private static string BuildTargetSnippetBlock(CodeProposalDraftRequest request)
    {
        if (request.SelectedTargetSnippet is not null)
        {
            return BuildSelectedTargetSnippetBlock(request.SelectedTargetSnippet);
        }

        var snippets = request.Context.Files
            .SelectMany(file => CodeTargetSnippetExtractor.Extract(file.RelativePath, file.Content))
            .Take(5)
            .ToArray();
        if (snippets.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Candidate method snippets from selected file.");
        builder.AppendLine("Choose exactly one candidate. Copy one candidate exactly as originalText. Add the XML documentation comment only in replacementText. Do not change the method body. Do not use proposedContent for this tiny edit.");
        for (var index = 0; index < snippets.Length; index++)
        {
            builder.AppendLine($"Candidate {index + 1} ({snippets[index].RelativePath}):");
            builder.AppendLine("```");
            builder.AppendLine(snippets[index].Text);
            builder.AppendLine("```");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildSelectedTargetSnippetBlock(CodeProposalSelectedTargetSnippet snippet)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Selected target snippet. The backend will use this exact text as originalText.");
        builder.AppendLine($"Selected target file: {snippet.RelativePath}");
        builder.AppendLine($"Selected target: {snippet.SignaturePreview}");
        builder.AppendLine("Return replacementText only for this exact snippet. Do not return originalText. Do not output proposedContent. Do not choose another target.");
        builder.AppendLine("```");
        builder.AppendLine(snippet.Text);
        builder.AppendLine("```");
        return builder.ToString().TrimEnd();
    }

    private static string SelectedTargetRetryInstruction(CodeProposalDraftRequest request) =>
        RequiresXmlDocComment(request.ChangeRequest.UserRequest)
            ? "Correction required: return only documentation.summary text. Do not include code, the method, ///, full file content, or behavior changes."
            : "Correction required: return replacementText only for the selected snippet.";

    private async Task<string> CollectProviderOutputAsync(
        IChatProvider provider,
        ChatRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(modelGenerationTimeout ?? DefaultModelGenerationTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            return await CollectAsync(provider, request, linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.ModelTimeout, exception);
        }
        catch (OperationCanceledException exception)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.ModelCancelled, exception);
        }
        catch (TimeoutException exception)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.ModelTimeout, exception);
        }
    }

    private static async Task<string> CollectAsync(
        IChatProvider provider,
        ChatRequest request,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        await foreach (var chunk in provider.StreamAsync(request, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                builder.Append(chunk.Content);
                if (builder.Length > MaxModelOutputCharacters)
                {
                    throw Failure(AedaCodeProposalCreationFailureReason.UnsafePatch);
                }
            }
        }

        return builder.ToString();
    }

    private static CodeProposalDraft ParseDraft(string output, CodeProposalDraftRequest request)
    {
        var selectedTargetMode = request.SelectedTargetSnippet is not null;
        var selectedXmlDocMode = selectedTargetMode &&
            RequiresXmlDocComment(request.ChangeRequest.UserRequest);
        var json = ExtractJsonObject(output);
        using var document = ParseJson(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw SchemaFailure("root_wrong_type");
        }

        ValidateRootProperties(root);
        var title = RequiredString(root, "title", "missing_title");
        var summary = RequiredString(root, "summary", "missing_summary");
        if (selectedXmlDocMode)
        {
            if (root.TryGetProperty("changes", out _) ||
                root.TryGetProperty("files", out _))
            {
                throw SchemaFailure("extra_keys");
            }

            var documentation = RequiredProperty(root, "documentation");
            if (documentation.ValueKind != JsonValueKind.Object)
            {
                throw SchemaFailure("documentation_wrong_type");
            }

            foreach (var property in documentation.EnumerateObject())
            {
                if (property.Name is not "summary")
                {
                    throw SchemaFailure("extra_keys");
                }
            }

            var docSummary = RequiredString(
                documentation,
                "summary",
                "invalid_xml_doc_summary");
            return new CodeProposalDraft(
                Bound(title, MaxTitleCharacters),
                Bound(summary, MaxSummaryCharacters),
                [
                    new PatchProposalFileEdit(
                        request.SelectedTargetSnippet!.RelativePath,
                        OriginalContent: null,
                        ProposedContent: docSummary,
                        PatchProposalFileChangeKind.Modify)
                ],
                []);
        }

        var changesElement = RequiredChanges(root);
        if (changesElement.ValueKind != JsonValueKind.Array)
        {
            throw SchemaFailure("changes_wrong_type");
        }

        if (changesElement.GetArrayLength() == 0)
        {
            throw SchemaFailure("empty_changes");
        }

        var files = new List<PatchProposalFileEdit>();
        foreach (var fileElement in changesElement.EnumerateArray())
        {
            if (fileElement.ValueKind != JsonValueKind.Object)
            {
                throw SchemaFailure("change_wrong_type");
            }

            ValidateChangeProperties(fileElement);
            var relativePath = NormalizeRelativePath(RequiredAliasedString(
                fileElement,
                ["relativePath", "path", "file", "filePath"],
                "change_missing_path"));
            var hasOriginalText = fileElement.TryGetProperty("originalText", out _);
            var hasReplacementText = HasAnyProperty(fileElement, ["replacementText", "replacement", "newText"]);
            var hasProposedContent = fileElement.TryGetProperty("proposedContent", out _);
            if (hasProposedContent && (hasOriginalText || hasReplacementText))
            {
                throw SchemaFailure("ambiguous_change_field");
            }

            if (selectedTargetMode)
            {
                if (hasProposedContent)
                {
                    throw SchemaFailure("selected_target_rejects_proposed_content");
                }

                if (!hasReplacementText)
                {
                    throw SchemaFailure("replacement_text_missing");
                }

                files.Add(new PatchProposalFileEdit(
                    relativePath,
                    OriginalContent: null,
                    ProposedContent: RequiredAliasedString(
                        fileElement,
                        ["replacementText", "replacement", "newText"],
                        "replacement_text_missing"),
                    PatchProposalFileChangeKind.Modify));
                continue;
            }

            if (hasOriginalText || hasReplacementText)
            {
                files.Add(new PatchProposalFileEdit(
                    relativePath,
                    RequiredString(fileElement, "originalText", "change_missing_original_text"),
                    RequiredAliasedString(
                        fileElement,
                        ["replacementText", "replacement", "newText"],
                        "change_missing_replacement"),
                    PatchProposalFileChangeKind.Modify));
                continue;
            }

            files.Add(new PatchProposalFileEdit(
                relativePath,
                OriginalContent: null,
                ProposedContent: RequiredString(fileElement, "proposedContent", "change_missing_replacement"),
                PatchProposalFileChangeKind.Modify));
        }

        var safeNotices = ReadSafeNotices(root);
        return new CodeProposalDraft(
            Bound(title, MaxTitleCharacters),
            Bound(summary, MaxSummaryCharacters),
            files,
            safeNotices);
    }

    private static JsonDocument ParseJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelJson, exception);
        }
    }

    private static string ExtractJsonObject(string output)
    {
        var trimmed = output.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelJson);
        }

        var fenced = TryExtractFencedJson(trimmed);
        var source = fenced ?? trimmed;
        var objects = FindTopLevelObjects(source);
        if (objects.Count != 1)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelJson);
        }

        var json = source.Substring(objects[0].Start, objects[0].Length).Trim();
        if (json.Length > MaxExtractedJsonCharacters)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.UnsafePatch);
        }

        return json;
    }

    private static string? TryExtractFencedJson(string value)
    {
        var firstFence = value.IndexOf("```", StringComparison.Ordinal);
        if (firstFence < 0)
        {
            return null;
        }

        var fenceLineEnd = value.IndexOf('\n', firstFence + 3);
        if (fenceLineEnd < 0)
        {
            return null;
        }

        var info = value[(firstFence + 3)..fenceLineEnd].Trim();
        if (!string.IsNullOrEmpty(info) &&
            !info.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var closingFence = value.IndexOf("```", fenceLineEnd + 1, StringComparison.Ordinal);
        return closingFence <= fenceLineEnd
            ? null
            : value[(fenceLineEnd + 1)..closingFence].Trim();
    }

    private static IReadOnlyList<(int Start, int Length)> FindTopLevelObjects(string value)
    {
        var objects = new List<(int Start, int Length)>();
        var depth = 0;
        var start = -1;
        var inString = false;
        var escaped = false;

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                if (depth == 0)
                {
                    start = index;
                }

                depth++;
                continue;
            }

            if (ch == '}')
            {
                if (depth == 0)
                {
                    return [];
                }

                depth--;
                if (depth == 0 && start >= 0)
                {
                    objects.Add((start, index - start + 1));
                    start = -1;
                }
            }
        }

        return depth == 0 ? objects : [];
    }

    private static JsonElement RequiredProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            throw SchemaFailure($"missing_{name}");
        }

        return property;
    }

    private static JsonElement RequiredChanges(JsonElement root)
    {
        var hasChanges = root.TryGetProperty("changes", out var changes);
        var hasFiles = root.TryGetProperty("files", out var files);
        if (hasChanges == hasFiles)
        {
            throw SchemaFailure(hasChanges ? "extra_keys" : "missing_changes");
        }

        return hasChanges ? changes : files;
    }

    private static void ValidateRootProperties(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("title" or "summary" or "changes" or "files" or "documentation" or "safeNotices"))
            {
                throw SchemaFailure("extra_keys");
            }
        }
    }

    private static void ValidateChangeProperties(JsonElement change)
    {
        foreach (var property in change.EnumerateObject())
        {
            if (property.Name is not (
                "relativePath" or "path" or "file" or "filePath" or
                "proposedContent" or "originalText" or "replacementText" or "replacement" or "newText"))
            {
                throw SchemaFailure("extra_keys");
            }
        }
    }

    private static bool HasAnyProperty(JsonElement element, IReadOnlyList<string> names) =>
        names.Any(name => element.TryGetProperty(name, out _));

    private static string RequiredString(JsonElement element, string name, string missingIssueCode)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            throw SchemaFailure(missingIssueCode);
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw SchemaFailure(missingIssueCode);
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw SchemaFailure(missingIssueCode);
        }

        return value;
    }

    private static string RequiredAliasedString(
        JsonElement element,
        IReadOnlyList<string> names,
        string missingIssueCode)
    {
        var matches = names
            .Where(name => element.TryGetProperty(name, out _))
            .ToArray();
        if (matches.Length == 0)
        {
            throw SchemaFailure(missingIssueCode);
        }

        if (matches.Length > 1)
        {
            throw SchemaFailure("ambiguous_change_field");
        }

        return RequiredString(element, matches[0], missingIssueCode);
    }

    private static IReadOnlyList<string> ReadSafeNotices(JsonElement root)
    {
        if (!root.TryGetProperty("safeNotices", out var notices))
        {
            return [];
        }

        if (notices.ValueKind != JsonValueKind.Array)
        {
            throw SchemaFailure("safe_notices_wrong_type");
        }

        return notices.EnumerateArray()
            .Select(notice => notice.ValueKind == JsonValueKind.String
                ? notice.GetString()
                : throw SchemaFailure("safe_notices_wrong_type"))
            .Where(notice => !string.IsNullOrWhiteSpace(notice))
            .Select(notice => Bound(notice, 160))
            .ToArray()!;
    }

    private static CodeProposalDraft ValidateDraft(
        CodeProposalDraft draft,
        CodeProposalDraftRequest request)
    {
        if (draft.FileEdits.Count == 0 || draft.FileEdits.Count > MaxFileEdits)
        {
            throw SchemaFailure(draft.FileEdits.Count == 0 ? "empty_changes" : "too_many_changes");
        }

        if (request.SelectedTargetSnippet is not null && draft.FileEdits.Count != 1)
        {
            throw SchemaFailure("selected_target_requires_single_change");
        }

        var allowedFiles = request.Context.Files
            .Select(file => new AllowedContextFile(
                NormalizeAllowedContextPath(file.RelativePath),
                file))
            .ToArray();
        var edits = new List<PatchProposalFileEdit>();

        foreach (var edit in draft.FileEdits)
        {
            var original = ResolveAllowedContextFile(edit.RelativePath, allowedFiles);
            if (request.SelectedTargetSnippet is not null)
            {
                if (!string.Equals(
                        NormalizeAllowedContextPath(request.SelectedTargetSnippet.RelativePath),
                        NormalizeAllowedContextPath(original.RelativePath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw Failure(AedaCodeProposalCreationFailureReason.UnsafeFileTarget);
                }

                var replacementText = RequiresXmlDocComment(request.ChangeRequest.UserRequest)
                    ? BuildXmlDocReplacement(
                        request.SelectedTargetSnippet.Text,
                        edit.ProposedContent)
                    : ValidateSelectedTargetReplacement(
                        request.SelectedTargetSnippet,
                        edit.ProposedContent,
                        request.ChangeRequest.UserRequest);
                var selectedContent = BuildTargetedReplacement(
                    original.Content,
                    request.SelectedTargetSnippet.Text,
                    replacementText);

                PatchDestructiveChangeGuard.RejectUnsafeEdit(edit with
                {
                    OriginalContent = original.Content,
                    ProposedContent = selectedContent
                });

                edits.Add(edit with
                {
                    RelativePath = original.RelativePath.Replace('\\', '/'),
                    OriginalContent = original.Content,
                    ProposedContent = selectedContent
                });
                continue;
            }

            var proposedContent = edit.OriginalContent is null
                ? edit.ProposedContent
                : BuildTargetedReplacement(
                    original.Content,
                    edit.OriginalContent,
                    edit.ProposedContent);

            if (string.IsNullOrEmpty(proposedContent) ||
                proposedContent.Length > MaxProposedFileCharacters ||
                proposedContent.Contains('\0'))
            {
                throw Failure(AedaCodeProposalCreationFailureReason.UnsafePatch);
            }

            PatchDestructiveChangeGuard.RejectUnsafeEdit(edit with
            {
                OriginalContent = original.Content,
                ProposedContent = proposedContent
            });

            edits.Add(edit with
            {
                RelativePath = original.RelativePath.Replace('\\', '/'),
                OriginalContent = original.Content,
                ProposedContent = proposedContent
            });
        }

        return draft with { FileEdits = edits };
    }

    private static string BuildTargetedReplacement(
        string fullContent,
        string originalText,
        string? replacementText)
    {
        if (string.IsNullOrWhiteSpace(originalText) ||
            string.IsNullOrEmpty(replacementText))
        {
            throw SchemaFailure("change_missing_replacement");
        }

        var first = fullContent.IndexOf(originalText, StringComparison.Ordinal);
        if (first < 0)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.TargetTextNotFound);
        }

        if (fullContent.IndexOf(originalText, first + originalText.Length, StringComparison.Ordinal) >= 0)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.AmbiguousTextReplacement);
        }

        return fullContent[..first] + replacementText + fullContent[(first + originalText.Length)..];
    }

    private static string ValidateSelectedTargetReplacement(
        CodeProposalSelectedTargetSnippet selected,
        string? replacementText,
        string userRequest)
    {
        if (string.IsNullOrWhiteSpace(replacementText))
        {
            throw SchemaFailure("replacement_text_missing");
        }

        var maxReplacementCharacters = Math.Max(selected.Text.Length * 3, selected.Text.Length + 2_000);
        if (replacementText.Length > maxReplacementCharacters ||
            replacementText.Contains('\0'))
        {
            throw Failure(AedaCodeProposalCreationFailureReason.UnsafePatch);
        }

        if (LooksLikeFullFileReplacement(selected.Text, replacementText) ||
            !ContainsSelectedSignature(selected, replacementText))
        {
            throw Failure(AedaCodeProposalCreationFailureReason.UnsafePatch);
        }

        if (RequiresXmlDocComment(userRequest))
        {
            ValidateXmlDocReplacement(selected.Text, replacementText);
        }

        return replacementText;
    }

    private static string BuildXmlDocReplacement(
        string selectedText,
        string? documentationSummary)
    {
        var summary = SanitizeXmlDocSummary(documentationSummary);
        var selected = NormalizeLineEndings(selectedText);
        var signatureLine = selected
            .Split('\n')
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;
        var indent = signatureLine[..^signatureLine.TrimStart().Length];
        var replacement = string.Join(
            "\n",
            [
                $"{indent}/// <summary>",
                $"{indent}/// {summary}",
                $"{indent}/// </summary>",
                selected
            ]);
        ValidateXmlDocReplacement(selected, replacement);
        return replacement;
    }

    private static string SanitizeXmlDocSummary(string? value)
    {
        var summary = NormalizeLineEndings(value ?? string.Empty)
            .Replace('\n', ' ')
            .Trim();
        while (summary.Contains("  ", StringComparison.Ordinal))
        {
            summary = summary.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(summary) ||
            summary.Length > 300 ||
            LooksLikeCode(summary))
        {
            throw SchemaFailure("invalid_xml_doc_summary");
        }

        return summary
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static bool LooksLikeCode(string value) =>
        value.Contains('{') ||
        value.Contains('}') ||
        value.Contains(';') ||
        value.Contains("=>", StringComparison.Ordinal) ||
        value.Contains("private ", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("public ", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("return ", StringComparison.OrdinalIgnoreCase);

    private static void ValidateXmlDocReplacement(string selectedText, string replacementText)
    {
        var selected = NormalizeLineEndings(selectedText);
        var replacement = NormalizeLineEndings(replacementText);
        if (!replacement.EndsWith(selected, StringComparison.Ordinal))
        {
            throw SchemaFailure("replacement_changed_method_body");
        }

        var documentation = replacement[..^selected.Length];
        if (string.IsNullOrWhiteSpace(documentation))
        {
            throw SchemaFailure("invalid_xml_doc_comment");
        }

        var signatureLine = selected.Split('\n').FirstOrDefault(line => line.Contains("private ", StringComparison.Ordinal)) ?? string.Empty;
        var indent = signatureLine[..^signatureLine.TrimStart().Length];
        var docLines = documentation.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (docLines.Length == 0 ||
            docLines.Any(line => !line.StartsWith(indent + "///", StringComparison.Ordinal)) ||
            docLines.Any(line => line.TrimStart().StartsWith("// ", StringComparison.Ordinal)))
        {
            throw SchemaFailure("invalid_xml_doc_comment");
        }

        if (!documentation.EndsWith('\n'))
        {
            throw SchemaFailure("replacement_indentation_changed");
        }
    }

    private static bool RequiresXmlDocComment(string request) =>
        Contains(request, "xml") ||
        Contains(request, "doc") ||
        Contains(request, "documentation");

    private static bool Contains(string value, string token) =>
        value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

    private static bool LooksLikeFullFileReplacement(string selectedText, string replacementText) =>
        !selectedText.Contains("class ", StringComparison.Ordinal) &&
        replacementText.Contains("\nclass ", StringComparison.Ordinal);

    private static bool ContainsSelectedSignature(
        CodeProposalSelectedTargetSnippet selected,
        string replacementText) =>
        replacementText.Contains(selected.SignaturePreview, StringComparison.Ordinal) ||
        (!string.IsNullOrWhiteSpace(selected.DisplayName) &&
            replacementText.Contains(selected.DisplayName + "(", StringComparison.Ordinal));

    private static string NormalizeRelativePath(string? value)
    {
        var path = (value ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) ||
            Path.IsPathRooted(path) ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            IsDriveQualified(path) ||
            path.Contains("..", StringComparison.Ordinal) ||
            path.Contains(':') ||
            path.Any(char.IsControl) ||
            path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.UnsafeFileTarget);
        }

        while (path.StartsWith("./", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw Failure(AedaCodeProposalCreationFailureReason.UnsafeFileTarget);
        }

        return path;
    }

    private static string NormalizeAllowedContextPath(string value)
    {
        var normalized = value.Trim().Replace('\\', '/').TrimStart('/');
        return string.IsNullOrWhiteSpace(normalized)
            ? throw Failure(AedaCodeProposalCreationFailureReason.UnsafeFileTarget)
            : normalized;
    }

    private static CodeContextFile ResolveAllowedContextFile(
        string modelTarget,
        IReadOnlyList<AllowedContextFile> allowedFiles)
    {
        var normalizedTarget = NormalizeRelativePath(modelTarget);
        var exactMatches = allowedFiles
            .Where(file => string.Equals(
                file.NormalizedRelativePath,
                normalizedTarget,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactMatches.Length == 1)
        {
            return exactMatches[0].File;
        }

        if (exactMatches.Length > 1)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.UnsafeFileTarget);
        }

        var suffix = "/" + normalizedTarget;
        var suffixMatches = allowedFiles
            .Where(file => file.NormalizedRelativePath.EndsWith(
                suffix,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return suffixMatches.Length == 1
            ? suffixMatches[0].File
            : throw Failure(AedaCodeProposalCreationFailureReason.UnsafeFileTarget);
    }

    private static bool IsDriveQualified(string path) =>
        path.Length >= 2 &&
        char.IsAsciiLetter(path[0]) &&
        path[1] == ':';

    private sealed record AllowedContextFile(
        string NormalizedRelativePath,
        CodeContextFile File);

    private static string Bound(string? value, int maxLength)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "Code proposal" : value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static bool IsPolicyRejection(string safeReasonCode) =>
        safeReasonCode is "remote_provider_disabled" or
            "private_network_provider_requires_approval" or
            "remote_workspace_context_denied" or
            "remote_memory_context_denied" or
            "remote_screenshot_context_denied" or
            "remote_clipboard_app_context_denied";

    private static bool CanRetry(AedaCodeProposalCreationFailureReason reason) =>
        reason is AedaCodeProposalCreationFailureReason.InvalidModelJson or
            AedaCodeProposalCreationFailureReason.InvalidModelSchema or
            AedaCodeProposalCreationFailureReason.TargetTextNotFound;

    private static AedaCodeProposalCreationException Failure(
        AedaCodeProposalCreationFailureReason reason,
        Exception? innerException = null) =>
        new(AedaCodeProposalCreationFailure.FromReason(reason), innerException);

    private static AedaCodeProposalCreationException SchemaFailure(
        string schemaIssueCode,
        Exception? innerException = null) =>
        new(
            AedaCodeProposalCreationFailure.FromReason(
                AedaCodeProposalCreationFailureReason.InvalidModelSchema,
                schemaIssueCode),
            innerException);

    private static AedaCodeProposalCreationException WithRetryAttempted(
        AedaCodeProposalCreationException exception) =>
        new(
            exception.Failure with { RetryAttempted = true },
            exception);

}
