namespace PersonalAI.Core.Context;

public sealed class ActiveWindowReferenceTracker
{
    public ActiveWindowReference? Current { get; private set; }

    public ActiveWindowReference? TryRemember(
        ActiveWindowReference? candidate,
        int ownProcessId,
        nint ownWindowHandle,
        bool isWindow)
    {
        if (candidate is null ||
            !isWindow ||
            candidate.WindowHandle == 0 ||
            candidate.WindowHandle == ownWindowHandle ||
            candidate.ProcessId == ownProcessId)
        {
            return Current;
        }

        Current = candidate;
        return Current;
    }

    public ActiveWindowReference? GetCurrentIfValid(bool isWindow)
    {
        return isWindow ? Current : null;
    }
}
