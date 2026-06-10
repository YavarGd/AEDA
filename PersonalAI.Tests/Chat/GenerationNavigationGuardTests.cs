using PersonalAI.Core.Chat;

namespace PersonalAI.Tests.Chat;

public sealed class GenerationNavigationGuardTests
{
    [Fact]
    public async Task NewChatWhileIdle_ProceedsWithoutConfirmationOrCancellation()
    {
        var guard = new GenerationNavigationGuard();
        var confirmed = false;
        var cancelled = false;

        var result = await guard.ConfirmStopAndProceedAsync(
            isGenerating: false,
            confirmStopAsync: () =>
            {
                confirmed = true;
                return Task.FromResult(false);
            },
            stopGenerationAsync: () =>
            {
                cancelled = true;
                return Task.CompletedTask;
            });

        Assert.Equal(GenerationNavigationResult.Proceed, result);
        Assert.False(confirmed);
        Assert.False(cancelled);
    }

    [Fact]
    public async Task NewChatWhileGenerating_UserDeclines_KeepsGenerating()
    {
        var guard = new GenerationNavigationGuard();
        var cancelled = false;

        var result = await guard.ConfirmStopAndProceedAsync(
            isGenerating: true,
            confirmStopAsync: () => Task.FromResult(false),
            stopGenerationAsync: () =>
            {
                cancelled = true;
                return Task.CompletedTask;
            });

        Assert.Equal(GenerationNavigationResult.Stay, result);
        Assert.False(cancelled);
    }

    [Fact]
    public async Task NewChatWhileGenerating_UserConfirms_StopsBeforeProceeding()
    {
        var guard = new GenerationNavigationGuard();
        var cancelled = false;

        var result = await guard.ConfirmStopAndProceedAsync(
            isGenerating: true,
            confirmStopAsync: () => Task.FromResult(true),
            stopGenerationAsync: () =>
            {
                cancelled = true;
                return Task.CompletedTask;
            });

        Assert.Equal(GenerationNavigationResult.Proceed, result);
        Assert.True(cancelled);
    }

    [Fact]
    public async Task SwitchingConversationWhileGenerating_UserDeclines_DoesNotSwitch()
    {
        var guard = new GenerationNavigationGuard();
        var switched = false;

        var result = await guard.ConfirmStopAndProceedAsync(
            isGenerating: true,
            confirmStopAsync: () => Task.FromResult(false),
            stopGenerationAsync: () => Task.CompletedTask);

        if (result == GenerationNavigationResult.Proceed)
        {
            switched = true;
        }

        Assert.Equal(GenerationNavigationResult.Stay, result);
        Assert.False(switched);
    }

    [Fact]
    public async Task SwitchingConversationWhileGenerating_UserConfirms_AllowsSwitch()
    {
        var guard = new GenerationNavigationGuard();
        var switched = false;

        var result = await guard.ConfirmStopAndProceedAsync(
            isGenerating: true,
            confirmStopAsync: () => Task.FromResult(true),
            stopGenerationAsync: () => Task.CompletedTask);

        if (result == GenerationNavigationResult.Proceed)
        {
            switched = true;
        }

        Assert.Equal(GenerationNavigationResult.Proceed, result);
        Assert.True(switched);
    }

    [Fact]
    public async Task DuplicateClicksWhileDialogIsOpen_DoNotOpenSecondDialog()
    {
        var guard = new GenerationNavigationGuard();
        var confirmationCount = 0;
        var confirmation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var first = guard.ConfirmStopAndProceedAsync(
            isGenerating: true,
            confirmStopAsync: () =>
            {
                confirmationCount++;
                return confirmation.Task;
            },
            stopGenerationAsync: () => Task.CompletedTask);

        var second = await guard.ConfirmStopAndProceedAsync(
            isGenerating: true,
            confirmStopAsync: () =>
            {
                confirmationCount++;
                return Task.FromResult(true);
            },
            stopGenerationAsync: () => Task.CompletedTask);

        confirmation.SetResult(false);
        var firstResult = await first;

        Assert.Equal(GenerationNavigationResult.Busy, second);
        Assert.Equal(GenerationNavigationResult.Stay, firstResult);
        Assert.Equal(1, confirmationCount);
    }

    [Fact]
    public async Task DuplicateCancellationRequests_RunStopGenerationOnce()
    {
        var guard = new GenerationNavigationGuard();
        var stopCount = 0;
        var stopCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var first = guard.ConfirmStopAndProceedAsync(
            isGenerating: true,
            confirmStopAsync: () => Task.FromResult(true),
            stopGenerationAsync: () =>
            {
                stopCount++;
                return stopCompletion.Task;
            });

        var second = await guard.ConfirmStopAndProceedAsync(
            isGenerating: true,
            confirmStopAsync: () => Task.FromResult(true),
            stopGenerationAsync: () =>
            {
                stopCount++;
                return Task.CompletedTask;
            });

        stopCompletion.SetResult();
        var firstResult = await first;

        Assert.Equal(GenerationNavigationResult.Busy, second);
        Assert.Equal(GenerationNavigationResult.Proceed, firstResult);
        Assert.Equal(1, stopCount);
    }

    [Fact]
    public async Task TargetActionExecutesOnlyAfterCancellationCompletes()
    {
        var guard = new GenerationNavigationGuard();
        var stopCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var targetExecuted = false;

        var navigation = guard.ConfirmStopAndProceedAsync(
            isGenerating: true,
            confirmStopAsync: () => Task.FromResult(true),
            stopGenerationAsync: () => stopCompletion.Task);

        await Task.Yield();
        Assert.False(targetExecuted);
        Assert.False(navigation.IsCompleted);

        stopCompletion.SetResult();

        if (await navigation == GenerationNavigationResult.Proceed)
        {
            targetExecuted = true;
        }

        Assert.True(targetExecuted);
    }

    [Fact]
    public async Task StreamedContentRemainsWithOriginalConversationUntilProceed()
    {
        var guard = new GenerationNavigationGuard();
        var originalConversation = Guid.NewGuid();
        var targetConversation = Guid.NewGuid();
        var activeConversation = originalConversation;
        var stopCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var navigation = guard.ConfirmStopAndProceedAsync(
            isGenerating: true,
            confirmStopAsync: () => Task.FromResult(true),
            stopGenerationAsync: () => stopCompletion.Task);

        await Task.Yield();
        Assert.Equal(originalConversation, activeConversation);

        stopCompletion.SetResult();

        if (await navigation == GenerationNavigationResult.Proceed)
        {
            activeConversation = targetConversation;
        }

        Assert.Equal(targetConversation, activeConversation);
    }
}
