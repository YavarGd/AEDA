using PersonalAI.Desktop.Avalonia.Views.Chat;

namespace PersonalAI.Tests.Avalonia.Views;

public sealed class AvaloniaChatConversationOpenerTests
{
    [Fact]
    public async Task Barrier_IsAwaitedAfterOpenAndBeforeReadingResult()
    {
        var order = new List<string>();
        var requested = Guid.NewGuid();

        var applied = await ChatConversationOpener.OpenAndConfirmAsync(
            requested,
            _ =>
            {
                order.Add("open");
                return Task.CompletedTask;
            },
            () =>
            {
                order.Add("barrier");
                return Task.CompletedTask;
            },
            () =>
            {
                order.Add("read");
                return requested;
            },
            CancellationToken.None);

        Assert.True(applied);
        Assert.Equal(["open", "barrier", "read"], order);
    }

    [Fact]
    public async Task ViewModelNoOp_IsReportedAsNotApplied()
    {
        // The view model silently declines missing rows and requests made while
        // generating, leaving a different conversation active.
        var applied = await ChatConversationOpener.OpenAndConfirmAsync(
            Guid.NewGuid(),
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(applied);
    }

    [Fact]
    public async Task NoActiveConversation_IsReportedAsNotApplied()
    {
        var applied = await ChatConversationOpener.OpenAndConfirmAsync(
            Guid.NewGuid(),
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            () => null,
            CancellationToken.None);

        Assert.False(applied);
    }

    [Fact]
    public async Task CancellationAfterBarrier_IsObservedInsteadOfClaimingSuccess()
    {
        var requested = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ChatConversationOpener.OpenAndConfirmAsync(
                requested,
                _ => Task.CompletedTask,
                () =>
                {
                    cancellation.Cancel();
                    return Task.CompletedTask;
                },
                () => requested,
                cancellation.Token));
    }
}
