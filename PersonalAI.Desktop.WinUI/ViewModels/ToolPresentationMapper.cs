using System.Text.Json;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed record ToolPermissionPresentation(
    string Title,
    string Action,
    string Explanation,
    string Scope,
    string TechnicalDetails,
    string ReadOnlyExplanation);

public sealed record ToolActivityPresentation(
    string Title,
    string State,
    string Detail,
    bool IsProblem);

public static class ToolPresentationMapper
{
    public const string ReadOnlyExplanation =
        "The assistant is requesting read-only access. It cannot modify files.";

    public static string FriendlyAction(ToolId? toolId)
    {
        return toolId?.ToString() switch
        {
            "workspace.directory.list" => "List files",
            "workspace.file.read_text" => "Read file",
            "workspace.text.search" => "Search text",
            "workspace.info.get" => "View workspace information",
            _ => "Use workspace tool"
        };
    }

    public static ToolPermissionPresentation ForPermission(
        PermissionRequest request)
    {
        var action = FriendlyAction(request.ToolId);
        var explanation = string.IsNullOrWhiteSpace(request.Explanation)
            ? "The assistant wants to use a workspace tool."
            : request.Explanation;

        return new ToolPermissionPresentation(
            request.Permissions.Contains(ToolPermission.ReadWorkspace)
                ? "Allow workspace access?"
                : "Allow tool access?",
            action,
            explanation,
            FormatScope(request.ResourceScope),
            string.IsNullOrWhiteSpace(request.ResourceScope)
                ? "No technical scope was provided."
                : request.ResourceScope,
            request.IsReadOnly
                ? ReadOnlyExplanation
                : "This request may change local state.");
    }

    public static ToolActivityPresentation ForTaskEvent(TaskEvent taskEvent)
    {
        var title = FriendlyAction(taskEvent.ToolId);
        var state = taskEvent.Kind switch
        {
            TaskEventKind.PermissionRequested => "Waiting for permission",
            TaskEventKind.PermissionGranted => "Permission approved",
            TaskEventKind.ToolRequested => "Requested",
            TaskEventKind.ToolStarted => "Running",
            TaskEventKind.ToolCompleted => "Completed",
            TaskEventKind.PermissionDenied => "Permission denied",
            TaskEventKind.ToolCancelled or TaskEventKind.TaskCancelled => "Cancelled",
            TaskEventKind.ToolTimedOut => "Failed",
            TaskEventKind.ToolFailed or TaskEventKind.TaskFailed => "Failed",
            _ => taskEvent.State?.ToString() ?? taskEvent.Kind.ToString()
        };

        var detail = taskEvent.Kind switch
        {
            TaskEventKind.PermissionDenied =>
                "The requested workspace access was not approved.",
            TaskEventKind.ToolCancelled or TaskEventKind.TaskCancelled =>
                "The workspace action was cancelled.",
            TaskEventKind.ToolTimedOut =>
                "The workspace action did not finish before its timeout.",
            _ => taskEvent.SafeErrorMessage ??
                taskEvent.ProgressLabel ??
                NormalizeSummary(taskEvent.Summary)
        };

        return new ToolActivityPresentation(
            title,
            state,
            detail,
            taskEvent.Kind is TaskEventKind.ToolFailed or
                TaskEventKind.ToolTimedOut or
                TaskEventKind.TaskFailed);
    }

    public static string RequestedActivity(
        ChatToolCall toolCall,
        IWorkspaceRegistry? workspaceRegistry)
    {
        return $"{FriendlyAction(new ToolId(toolCall.Name))} requested\n{FormatCallTarget(toolCall, workspaceRegistry)}";
    }

    public static string CompletedActivity(
        ChatToolCall toolCall,
        ChatToolResultPayload result,
        IWorkspaceRegistry? workspaceRegistry)
    {
        if (result.Status == ToolExecutionStatus.PermissionDenied.ToString())
        {
            return "Permission denied\nThe requested workspace access was not approved.";
        }

        if (result.Status == ToolExecutionStatus.Cancelled.ToString())
        {
            return "Cancelled\nThe workspace action was cancelled.";
        }

        if (!result.IsSuccess)
        {
            return $"{SafeFailureTitle(result.SafeErrorCode)}\n{result.SafeErrorMessage ?? "The workspace action could not be completed."}";
        }

        var suffix = result.IsTruncated ? " - Result truncated" : string.Empty;
        var resultSummary = FormatResultSummary(result);
        return string.IsNullOrWhiteSpace(resultSummary)
            ? $"{FriendlyAction(new ToolId(toolCall.Name))} completed{suffix}\n{FormatCallTarget(toolCall, workspaceRegistry)}"
            : $"{FriendlyAction(new ToolId(toolCall.Name))} completed{suffix}\n{FormatCallTarget(toolCall, workspaceRegistry)} - {resultSummary}";
    }

    public static string FormatStoredToolActivity(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            if (root.TryGetProperty("kind", out var kind) &&
                string.Equals(kind.GetString(), "tool_call", StringComparison.Ordinal))
            {
                var toolName = root.TryGetProperty("toolName", out var tool)
                    ? tool.GetString()
                    : null;
                return $"{FriendlyAction(ToToolId(toolName))} requested.";
            }

            if (root.TryGetProperty("toolName", out var resultTool))
            {
                var action = FriendlyAction(ToToolId(resultTool.GetString()));
                var status = root.TryGetProperty("status", out var statusElement)
                    ? statusElement.GetString()
                    : null;
                var isTruncated =
                    root.TryGetProperty("isTruncated", out var truncated) &&
                    truncated.ValueKind == JsonValueKind.True;
                var safeError = root.TryGetProperty("safeErrorMessage", out var error)
                    ? error.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(safeError))
                {
                    return safeError;
                }

                return isTruncated
                    ? $"{action} completed - Result truncated"
                    : $"{action} {status?.ToLowerInvariant() ?? "completed"}.";
            }
        }
        catch (JsonException)
        {
        }

        return "Workspace tool activity.";
    }

    public static string FormatScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return "Workspace resource";
        }

        var parts = scope.Split(':', 3);
        if (parts.Length == 3 &&
            string.Equals(parts[0], "WORKSPACE", StringComparison.OrdinalIgnoreCase))
        {
            return parts[2] == "."
                ? "Workspace root"
                : $"Workspace path: {parts[2]}";
        }

        return "Workspace resource";
    }

    private static string FormatCallTarget(
        ChatToolCall toolCall,
        IWorkspaceRegistry? workspaceRegistry)
    {
        var workspace = GetString(toolCall.Arguments, "workspaceId");
        var workspaceName = workspace;

        if (!string.IsNullOrWhiteSpace(workspace) &&
            workspaceRegistry?.TryGet(new WorkspaceId(workspace), out var descriptor) == true)
        {
            workspaceName = descriptor.DisplayName;
        }

        var relativePath = FirstNonEmpty(
            GetString(toolCall.Arguments, "relativePath"),
            GetString(toolCall.Arguments, "relativeDirectory"));
        var query = GetString(toolCall.Arguments, "query");

        if (!string.IsNullOrWhiteSpace(query))
        {
            return string.IsNullOrWhiteSpace(relativePath)
                ? $"Search for \"{query}\" in {workspaceName}"
                : $"Search for \"{query}\" under {FormatRelativeDisplay(relativePath)} in {workspaceName}";
        }

        return string.IsNullOrWhiteSpace(relativePath)
            ? $"Workspace: {workspaceName}"
            : $"{FormatRelativeDisplay(relativePath)} in {workspaceName}";
    }

    private static string FormatRelativeDisplay(string value)
    {
        if (Path.IsPathRooted(value) ||
            value.StartsWith(@"\\", StringComparison.Ordinal) ||
            value.StartsWith("//", StringComparison.Ordinal) ||
            value.Split(['\\', '/']).Any(segment =>
                segment.Contains(':', StringComparison.Ordinal)))
        {
            return "Requested path";
        }

        return value;
    }

    private static string FormatResultSummary(ChatToolResultPayload result)
    {
        if (result.Output is null)
        {
            return string.Empty;
        }

        var output = result.Output.Value;
        if (output.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (output.TryGetProperty("matches", out var matches) &&
            matches.ValueKind == JsonValueKind.Array)
        {
            return $"{matches.GetArrayLength()} matches";
        }

        if (output.TryGetProperty("entries", out var entries) &&
            entries.ValueKind == JsonValueKind.Array)
        {
            return $"{entries.GetArrayLength()} entries";
        }

        return string.Empty;
    }

    private static string SafeFailureTitle(string? safeErrorCode)
    {
        return safeErrorCode switch
        {
            "permission_denied" => "Permission denied",
            "workspace_not_found" => "Workspace could not be resolved",
            "workspace_unavailable" => "Workspace is unavailable",
            "workspace_ambiguous" => "Choose a workspace",
            "invalid_tool_arguments" => "Tool arguments were invalid",
            "permission_requirements_failed" => "Permission request could not be opened",
            "permission_broker_failed" => "Permission request could not be opened",
            "path_outside_workspace" => "Access blocked",
            "invalid_relative_path" => "Access blocked",
            "reparse_point_rejected" => "Access blocked",
            _ => "Workspace tool failed"
        };
    }

    private static ToolId? ToToolId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new ToolId(value);

    private static string NormalizeSummary(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Workspace activity updated.";
        }

        return value.Replace("Tool", "Workspace tool", StringComparison.Ordinal);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
