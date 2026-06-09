namespace PersonalAI.Core.Context;

public sealed class AttachedContextState
{
    public ActiveApplicationContext? Current { get; private set; }

    public bool HasContext => Current is not null;

    public void Attach(ActiveApplicationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Current = context;
    }

    public ActiveApplicationContext? Remove()
    {
        var removed = Current;
        Current = null;
        return removed;
    }
}
