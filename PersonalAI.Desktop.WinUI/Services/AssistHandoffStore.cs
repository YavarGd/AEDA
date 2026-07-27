namespace PersonalAI.Desktop.WinUI.Services;

public sealed class AssistHandoffStore
{
    private AssistHandoffPayload? _current;

    public void Store(AssistHandoffPayload payload)
    {
        _current = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    public bool TryRead(out AssistHandoffPayload? payload)
    {
        payload = _current;
        return _current is not null;
    }

    public AssistHandoffPayload? TryConsume()
    {
        var payload = _current;
        _current = null;
        return payload;
    }

    public void Clear()
    {
        _current = null;
    }
}
