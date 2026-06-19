using PersonalAI.Core.Chat;

namespace PersonalAI.Core.Providers;

public sealed record ContextPrivacyFilterRequest(
    ProviderProfile Provider,
    IReadOnlyList<ChatMessage> Messages,
    bool AllowWorkspaceContext,
    bool AllowMemoryContext,
    bool AllowScreenshots,
    bool AllowClipboardOrAppContext);

public sealed record ContextPrivacyFilterResult(
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<string> RemovedSafeSummaries);

public interface IContextPrivacyFilter
{
    Task<ContextPrivacyFilterResult> FilterAsync(
        ContextPrivacyFilterRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ContextPrivacyFilter : IContextPrivacyFilter
{
    private static readonly string[] WorkspaceSignals =
    [
        "[workspace]",
        "workspace context",
        "workspace file",
        "retrieved workspace"
    ];

    private static readonly string[] MemorySignals =
    [
        "[memory]",
        "memory context",
        "retrieval context",
        "rag context"
    ];

    private static readonly string[] ClipboardSignals =
    [
        "[clipboard]",
        "clipboard context",
        "active window",
        "app context"
    ];

    public Task<ContextPrivacyFilterResult> FilterAsync(
        ContextPrivacyFilterRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        if (request.Provider.IsLocal)
        {
            return Task.FromResult(new ContextPrivacyFilterResult(request.Messages, []));
        }

        var filtered = new List<ChatMessage>();
        var summaries = new List<string>();

        foreach (var message in request.Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var removeText = ShouldRemoveText(message.Content, request, out var reason);
            var removeImages = message.Images.Count > 0 && !request.AllowScreenshots;

            if (removeText)
            {
                summaries.Add(reason);
            }

            if (removeImages)
            {
                summaries.Add("screenshot_context_removed");
            }

            if (removeText && removeImages)
            {
                continue;
            }

            filtered.Add(new ChatMessage(
                message.Role,
                removeText ? string.Empty : message.Content,
                removeImages ? [] : message.Images,
                message.ToolCalls,
                message.ToolCallId,
                message.ToolName));
        }

        return Task.FromResult(new ContextPrivacyFilterResult(
            filtered,
            summaries.Distinct(StringComparer.Ordinal).ToArray()));
    }

    private static bool ShouldRemoveText(
        string content,
        ContextPrivacyFilterRequest request,
        out string reason)
    {
        if (!request.AllowWorkspaceContext && ContainsAny(content, WorkspaceSignals))
        {
            reason = "workspace_context_removed";
            return true;
        }

        if (!request.AllowMemoryContext && ContainsAny(content, MemorySignals))
        {
            reason = "memory_context_removed";
            return true;
        }

        if (!request.AllowClipboardOrAppContext &&
            ContainsAny(content, ClipboardSignals))
        {
            reason = "clipboard_app_context_removed";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool ContainsAny(string content, IReadOnlyList<string> signals) =>
        signals.Any(signal => content.Contains(signal, StringComparison.OrdinalIgnoreCase));
}
