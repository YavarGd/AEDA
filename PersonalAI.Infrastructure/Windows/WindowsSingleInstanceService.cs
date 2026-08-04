#if WINDOWS
namespace PersonalAI.Infrastructure.Windows;

public sealed class WindowsSingleInstanceService : IDisposable
{
    public const string MutexName = "Local\\PersonalAI.WinUI.SingleInstance";

    private readonly Mutex _mutex;
    private bool _ownsMutex;

    public WindowsSingleInstanceService() : this(MutexName)
    {
    }

    internal WindowsSingleInstanceService(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        _mutex = new Mutex(
            initiallyOwned: true,
            name: mutexName,
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
#endif
