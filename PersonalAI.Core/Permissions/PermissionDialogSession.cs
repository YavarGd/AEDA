namespace PersonalAI.Core.Permissions;

public sealed class PermissionDialogSession
{
    private readonly object _gate = new();
    private PermissionDialogOutcome? _outcome;
    private Func<Task>? _closeAsync;

    public bool TrySetOutcome(PermissionDialogOutcome outcome)
    {
        lock (_gate)
        {
            if (_outcome is not null)
            {
                return false;
            }

            _outcome = outcome;
            return true;
        }
    }

    public PermissionDialogOutcome OutcomeOr(PermissionDialogOutcome fallback)
    {
        lock (_gate)
        {
            return _outcome ?? fallback;
        }
    }

    public async Task RegisterCloseAsync(Func<Task> closeAsync)
    {
        ArgumentNullException.ThrowIfNull(closeAsync);
        var shouldClose = false;

        lock (_gate)
        {
            _closeAsync = closeAsync;
            shouldClose = _outcome is
                PermissionDialogOutcome.CancelTask or
                PermissionDialogOutcome.Unavailable;
        }

        if (shouldClose)
        {
            await TryCloseAsync(closeAsync);
        }
    }

    public async Task RequestCloseAsync(PermissionDialogOutcome outcome)
    {
        TrySetOutcome(outcome);

        Func<Task>? closeAsync;
        lock (_gate)
        {
            closeAsync = _closeAsync;
        }

        if (closeAsync is not null)
        {
            await TryCloseAsync(closeAsync);
        }
    }

    private async Task TryCloseAsync(Func<Task> closeAsync)
    {
        try
        {
            await closeAsync();
        }
        catch
        {
            TrySetOutcome(PermissionDialogOutcome.Error);
        }
    }
}
