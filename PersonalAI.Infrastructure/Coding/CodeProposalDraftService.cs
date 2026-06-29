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
            draft = ValidateDraft(ParseDraft(output), request.Context);
        }
        catch (AedaCodeProposalCreationException exception)
            when (CanRetry(exception.Failure.Reason))
        {
            var schemaIssueCode = exception.Failure.SchemaIssueCode;
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
                draft = ValidateDraft(ParseDraft(retryOutput), request.Context);
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
        var builder = new StringBuilder();
        builder.AppendLine("[workspace] Bounded workspace context for proposal creation.");
        builder.AppendLine("Return only one JSON object.");
        builder.AppendLine("No markdown. No prose. No code fences. No extra keys.");
        builder.AppendLine("Use this exact schema:");
        builder.AppendLine("{\"title\":\"short title\",\"summary\":\"safe summary\",\"changes\":[{\"relativePath\":\"path/from/allowed/list\",\"proposedContent\":\"complete proposed file content\"}],\"safeNotices\":[]}");
        builder.AppendLine("Required fields: title, summary, changes, relativePath, proposedContent.");
        builder.AppendLine("Rules:");
        builder.AppendLine("- changes must be a non-empty array.");
        builder.AppendLine("- Use exactly one of the allowed relative paths below as relativePath.");
        builder.AppendLine("- Do not shorten file names.");
        builder.AppendLine("- Do not use only the basename unless it appears exactly that way in the allowed list.");
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
        string previousOutput,
        string? schemaIssueCode)
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
                        $"Safe schema issue code: {schemaIssueCode ?? "unknown_schema_issue"}",
                        "Use exactly one of these allowed relativePath values. Do not shorten file names.",
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
            throw SchemaFailure("root_wrong_type");
        }

        ValidateRootProperties(root);
        var title = RequiredString(root, "title", "missing_title");
        var summary = RequiredString(root, "summary", "missing_summary");
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
            files.Add(new PatchProposalFileEdit(
                NormalizeRelativePath(RequiredAliasedString(
                    fileElement,
                    ["relativePath", "path", "file", "filePath"],
                    "change_missing_path")),
                OriginalContent: null,
                ProposedContent: RequiredAliasedString(
                    fileElement,
                    ["proposedContent", "replacement", "newText"],
                    "change_missing_replacement"),
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
            if (property.Name is not ("title" or "summary" or "changes" or "files" or "safeNotices"))
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
                "proposedContent" or "replacement" or "newText"))
            {
                throw SchemaFailure("extra_keys");
            }
        }
    }

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
        CodeContextPack context)
    {
        if (draft.FileEdits.Count == 0 || draft.FileEdits.Count > MaxFileEdits)
        {
            throw SchemaFailure(draft.FileEdits.Count == 0 ? "empty_changes" : "too_many_changes");
        }

        var allowedFiles = context.Files
            .Select(file => new AllowedContextFile(
                NormalizeAllowedContextPath(file.RelativePath),
                file))
            .ToArray();
        var edits = new List<PatchProposalFileEdit>();

        foreach (var edit in draft.FileEdits)
        {
            var original = ResolveAllowedContextFile(edit.RelativePath, allowedFiles);

            if (string.IsNullOrEmpty(edit.ProposedContent) ||
                edit.ProposedContent.Length > MaxProposedFileCharacters ||
                edit.ProposedContent.Contains('\0'))
            {
                throw Failure(AedaCodeProposalCreationFailureReason.UnsafePatch);
            }

            edits.Add(edit with
            {
                RelativePath = original.RelativePath.Replace('\\', '/'),
                OriginalContent = original.Content
            });
        }

        return draft with { FileEdits = edits };
    }

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
            AedaCodeProposalCreationFailureReason.InvalidModelSchema;

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
