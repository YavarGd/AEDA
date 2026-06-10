namespace PersonalAI.Core.Chat;

public sealed class GenerationNavigationGuard
{
    private bool _isConfirmationOpen;
    private bool _isCancellationInProgress;

    public async Task<GenerationNavigationResult> ConfirmStopAndProceedAsync(
        bool isGenerating,
        Func<Task<bool>> confirmStopAsync,
        Func<Task> stopGenerationAsync,
        Action? stopping = null)
    {
        ArgumentNullException.ThrowIfNull(confirmStopAsync);
        ArgumentNullException.ThrowIfNull(stopGenerationAsync);

        if (!isGenerating)
        {
            return GenerationNavigationResult.Proceed;
        }

        if (_isConfirmationOpen || _isCancellationInProgress)
        {
            return GenerationNavigationResult.Busy;
        }

        _isConfirmationOpen = true;
        bool shouldStop;

        try
        {
            shouldStop = await confirmStopAsync();
        }
        finally
        {
            _isConfirmationOpen = false;
        }

        if (!shouldStop)
        {
            return GenerationNavigationResult.Stay;
        }

        _isCancellationInProgress = true;

        try
        {
            stopping?.Invoke();
            await stopGenerationAsync();
            return GenerationNavigationResult.Proceed;
        }
        finally
        {
            _isCancellationInProgress = false;
        }
    }
}
