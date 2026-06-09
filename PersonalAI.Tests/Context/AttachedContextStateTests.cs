using PersonalAI.Core.Context;

namespace PersonalAI.Tests.Context;

public sealed class AttachedContextStateTests
{
    [Fact]
    public void Remove_ClearsAndReturnsAttachedContext()
    {
        var state = new AttachedContextState();
        var context = new ActiveApplicationContext(
            100,
            200,
            "notepad",
            null,
            "notes",
            null,
            null,
            null,
            DateTimeOffset.UtcNow);

        state.Attach(context);
        var removed = state.Remove();

        Assert.Same(context, removed);
        Assert.False(state.HasContext);
        Assert.Null(state.Current);
    }
}
