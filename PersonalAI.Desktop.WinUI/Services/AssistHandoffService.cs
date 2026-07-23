namespace PersonalAI.Desktop.WinUI.Services;

public sealed class AssistHandoffService(AssistHandoffStore store)
{
    public void Handoff(
        string userRequest,
        AssistContextEnvelope? context,
        string? originApplication,
        string? currentResponse,
        Guid? conversationId,
        AssistHandoffDestination destination)
    {
        var payload = new AssistHandoffPayload(
            userRequest,
            context,
            originApplication,
            currentResponse,
            conversationId,
            destination);

        store.Store(payload);
    }

    public AssistHandoffPayload? TryConsume() => store.TryConsume();

    public bool TryPeek(out AssistHandoffPayload? payload) =>
        store.TryRead(out payload);
}
