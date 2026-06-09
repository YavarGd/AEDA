using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PersonalAI.Desktop.Windows;

public sealed class GlobalHotKeyService : IDisposable
{
    private const int WmHotKey = 0x0312;

    private readonly HwndSource _source;
    private readonly GlobalHotKey _hotKey;
    private bool _isRegistered;

    public event EventHandler? HotKeyPressed;

    public GlobalHotKeyService(nint windowHandle, GlobalHotKey hotKey)
    {
        if (windowHandle == 0)
        {
            throw new ArgumentException(
                "A valid window handle is required.",
                nameof(windowHandle));
        }

        _hotKey = hotKey;
        _source = HwndSource.FromHwnd(windowHandle) ??
            throw new InvalidOperationException(
                "Could not create a WPF message source for the window.");
        _source.AddHook(WndProc);
    }

    public bool Register()
    {
        if (_isRegistered)
        {
            return true;
        }

        _isRegistered = RegisterHotKey(
            _source.Handle,
            _hotKey.Id,
            (uint)_hotKey.Modifiers,
            _hotKey.VirtualKey);

        return _isRegistered;
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);

        if (_isRegistered)
        {
            UnregisterHotKey(_source.Handle, _hotKey.Id);
            _isRegistered = false;
        }
    }

    private nint WndProc(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmHotKey && wParam.ToInt32() == _hotKey.Id)
        {
            HotKeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(
        nint hWnd,
        int id,
        uint fsModifiers,
        uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
