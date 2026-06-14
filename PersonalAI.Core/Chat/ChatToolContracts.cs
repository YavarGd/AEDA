using System.Text.Json;

namespace PersonalAI.Core.Chat;

public sealed record ChatToolDefinition(
    string Name,
    string Description,
    JsonElement Parameters);

public sealed record ChatToolCall(
    string Id,
    string Name,
    JsonElement Arguments);

public sealed record ChatToolResultPayload(
    string ToolCallId,
    string ToolName,
    bool IsSuccess,
    string Status,
    string Summary,
    string? SafeErrorCode,
    string? SafeErrorMessage,
    JsonElement? Output,
    bool IsTruncated);

public interface IToolCallingChatProvider : IChatProvider
{
    bool SupportsToolCalls => true;
}
