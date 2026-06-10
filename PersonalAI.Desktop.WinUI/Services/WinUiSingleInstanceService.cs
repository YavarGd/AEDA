namespace PersonalAI.Desktop.WinUI.Services;

public sealed class WinUiSingleInstanceService : IDisposable
{
    public const string MutexName = "Local\\PersonalAI.WinUI.SingleInstance";

    private readonly Mutex _mutex;
    private bool _ownsMutex;

    public WinUiSingleInstanceService()
    {
        _mutex = new Mutex(
            initiallyOwned: true,
            name: MutexName,
            createdNew: out var createdNew);
        _ownsMutex = createdNew;
    }

    public bool IsPrimaryInstance => _ownsMutex;

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}
