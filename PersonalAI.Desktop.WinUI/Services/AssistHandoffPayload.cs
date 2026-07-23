namespace PersonalAI.Desktop.WinUI.Services;

public sealed record AssistHandoffPayload(
    string UserRequest,
    AssistContextEnvelope? Context,
    string? OriginApplication,
    string? CurrentResponse,
    Guid? ConversationId,
    AssistHandoffDestination Destination)
{
    public bool HasContext => Context?.HasContext == true;

    public string SafeActivitySummary =>
        AssistActivitySummary.CreateSafeSummary(Context, Destination.ToString().ToLowerInvariant());
}
