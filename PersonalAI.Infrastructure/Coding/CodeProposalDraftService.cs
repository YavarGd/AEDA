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
    Func<ProviderRoutingSettings> providerRoutingSettingsProvider) : ICodeProposalDraftService
{
    private const int MaxRequestCharacters = 4_000;
    private const int MaxTitleCharacters = 120;
    private const int MaxSummaryCharacters = 700;
    private const int MaxModelOutputCharacters = 240_000;
    private const int MaxExtractedJsonCharacters = 220_000;
    private const int MaxFileEdits = 12;
    private const int MaxProposedFileCharacters = 160_000;

    public async Task<CodeProposalDraft> CreateDraftAsync(
        CodeProposalDraftRequest request,
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

        var chatRequest = new ChatRequest(decision.Model.ModelId.Value, filtered.Messages);
        var output = await CollectProviderOutputAsync(provider, chatRequest, cancellationToken)
            .ConfigureAwait(false);
        CodeProposalDraft draft;
        try
        {
            draft = ValidateDraft(ParseDraft(output), request.Context);
        }
        catch (AedaCodeProposalCreationException exception)
            when (CanRetry(exception.Failure.Reason))
        {
            var retryMessages = BuildRetryMessages(request, output);
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
            draft = ValidateDraft(ParseDraft(retryOutput), request.Context);
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
        var builder = new StringBuilder();
        builder.AppendLine("[workspace] Bounded workspace context for proposal creation.");
        builder.AppendLine("Return only one JSON object.");
        builder.AppendLine("No markdown. No prose. No code fences. No extra keys.");
        builder.AppendLine("Use this exact schema:");
        builder.AppendLine("{\"title\":\"short title\",\"summary\":\"safe summary\",\"changes\":[{\"relativePath\":\"path/from/allowed/list\",\"proposedContent\":\"complete proposed file content\"}],\"safeNotices\":[]}");
        builder.AppendLine("Rules:");
        builder.AppendLine("- changes must be a non-empty array.");
        builder.AppendLine("- relativePath must exactly match one allowed context path.");
        builder.AppendLine("- Do not invent file paths.");
        builder.AppendLine("- Do not delete files.");
        builder.AppendLine("- Keep changes minimal.");
        builder.AppendLine("- Do not include unchanged full files unless needed to represent the complete proposed file content.");
        builder.AppendLine("- The proposal is not applied.");
        builder.AppendLine();
        builder.AppendLine("Allowed context paths:");
        foreach (var path in request.Context.Files.Select(file => file.RelativePath))
        {
            builder.AppendLine($"- {path}");
        }

        builder.AppendLine();
        builder.AppendLine("Valid example:");
        builder.AppendLine("{\"title\":\"Add XML docs\",\"summary\":\"Adds a documentation comment without behavior changes.\",\"changes\":[{\"relativePath\":\"src/Example.cs\",\"proposedContent\":\"/// <summary>Explains the helper.</summary>\\nvoid Helper() {}\\n\"}],\"safeNotices\":[]}");
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
        string previousOutput)
    {
        var allowedPaths = string.Join(", ", request.Context.Files.Select(file => file.RelativePath));
        return
        [
            new ChatMessage(
                ChatRole.System,
                "Reformat the previous proposal draft into exactly one valid JSON object. Do not add new changes."),
            new ChatMessage(
                ChatRole.User,
                string.Join(
                    Environment.NewLine,
                    [
                        "[workspace] Proposal draft JSON repair request.",
                        "Return only one JSON object. No markdown. No prose. No code fences.",
                        "Schema: {\"title\":\"short title\",\"summary\":\"safe summary\",\"changes\":[{\"relativePath\":\"allowed path\",\"proposedContent\":\"complete proposed file content\"}],\"safeNotices\":[]}",
                        $"Allowed relativePath values: {allowedPaths}",
                        "If the previous output had explanations, remove them.",
                        "If a field is missing, infer only from the previous output and original request.",
                        "Original request:",
                        Bound(request.ChangeRequest.UserRequest, MaxRequestCharacters),
                        "Previous output to reformat:",
                        Bound(previousOutput, 20_000)
                    ]))
        ];
    }

    private static async Task<string> CollectProviderOutputAsync(
        IChatProvider provider,
        ChatRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CollectAsync(provider, request, cancellationToken)
                .ConfigureAwait(false);
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

    private static CodeProposalDraft ParseDraft(string output)
    {
        var json = ExtractJsonObject(output);
        using var document = ParseJson(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelSchema);
        }

        ValidateRootProperties(root);
        var title = RequiredString(root, "title");
        var summary = RequiredString(root, "summary");
        var changesElement = RequiredChanges(root);
        if (changesElement.ValueKind != JsonValueKind.Array)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelSchema);
        }

        var files = new List<PatchProposalFileEdit>();
        foreach (var fileElement in changesElement.EnumerateArray())
        {
            if (fileElement.ValueKind != JsonValueKind.Object)
            {
                throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelSchema);
            }

            ValidateChangeProperties(fileElement);
            files.Add(new PatchProposalFileEdit(
                NormalizeRelativePath(RequiredString(fileElement, "relativePath")),
                OriginalContent: null,
                ProposedContent: RequiredString(fileElement, "proposedContent"),
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
            throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelSchema);
        }

        return property;
    }

    private static JsonElement RequiredChanges(JsonElement root)
    {
        var hasChanges = root.TryGetProperty("changes", out var changes);
        var hasFiles = root.TryGetProperty("files", out var files);
        if (hasChanges == hasFiles)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelSchema);
        }

        return hasChanges ? changes : files;
    }

    private static void ValidateRootProperties(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("title" or "summary" or "changes" or "files" or "safeNotices"))
            {
                throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelSchema);
            }
        }
    }

    private static void ValidateChangeProperties(JsonElement change)
    {
        foreach (var property in change.EnumerateObject())
        {
            if (property.Name is not ("relativePath" or "proposedContent"))
            {
                throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelSchema);
            }
        }
    }

    private static string RequiredString(JsonElement element, string name)
    {
        var property = RequiredProperty(element, name);
        if (property.ValueKind != JsonValueKind.String)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelSchema);
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelSchema);
        }

        return value;
    }

    private static IReadOnlyList<string> ReadSafeNotices(JsonElement root)
    {
        if (!root.TryGetProperty("safeNotices", out var notices))
        {
            return [];
        }

        if (notices.ValueKind != JsonValueKind.Array)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelSchema);
        }

        return notices.EnumerateArray()
            .Select(notice => notice.ValueKind == JsonValueKind.String
                ? notice.GetString()
                : throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelSchema))
            .Where(notice => !string.IsNullOrWhiteSpace(notice))
            .Select(notice => Bound(notice, 160))
            .ToArray()!;
    }

    private static CodeProposalDraft ValidateDraft(
        CodeProposalDraft draft,
        CodeContextPack context)
    {
        if (draft.FileEdits.Count == 0 || draft.FileEdits.Count > MaxFileEdits)
        {
            throw Failure(AedaCodeProposalCreationFailureReason.InvalidModelSchema);
        }

        var contextByPath = context.Files.ToDictionary(
            file => file.RelativePath.Replace('\\', '/'),
            StringComparer.OrdinalIgnoreCase);
        var edits = new List<PatchProposalFileEdit>();

        foreach (var edit in draft.FileEdits)
        {
            if (!contextByPath.TryGetValue(edit.RelativePath, out var original))
            {
                throw Failure(AedaCodeProposalCreationFailureReason.UnsafeFileTarget);
            }

            if (string.IsNullOrEmpty(edit.ProposedContent) ||
                edit.ProposedContent.Length > MaxProposedFileCharacters ||
                edit.ProposedContent.Contains('\0'))
            {
                throw Failure(AedaCodeProposalCreationFailureReason.UnsafePatch);
            }

            edits.Add(edit with { OriginalContent = original.Content });
        }

        return draft with { FileEdits = edits };
    }

    private static string NormalizeRelativePath(string? value)
    {
        var path = (value ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) ||
            Path.IsPathRooted(path) ||
            path.Contains("..", StringComparison.Ordinal))
        {
            throw Failure(AedaCodeProposalCreationFailureReason.UnsafeFileTarget);
        }

        return path.TrimStart('/');
    }

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
            AedaCodeProposalCreationFailureReason.InvalidModelSchema;

    private static AedaCodeProposalCreationException Failure(
        AedaCodeProposalCreationFailureReason reason,
        Exception? innerException = null) =>
        new(AedaCodeProposalCreationFailure.FromReason(reason), innerException);

}
