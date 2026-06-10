using System.Runtime.InteropServices;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class WinUiGlobalHotKeyService : NativeMessageWindow
{
    private const uint WmHotKey = 0x0312;
    private readonly int _id;
    private readonly uint _modifiers;
    private readonly uint _virtualKey;
    private bool _isRegistered;

    public WinUiGlobalHotKeyService(int id, uint modifiers, uint virtualKey)
        : base("PersonalAI.WinUI.MessageWindow")
    {
        _id = id;
        _modifiers = modifiers;
        _virtualKey = virtualKey;
    }

    public event EventHandler? HotKeyPressed;

    public bool IsRegistered => _isRegistered;

    public bool Register()
    {
        if (_isRegistered)
        {
            return true;
        }

        _isRegistered = RegisterHotKey(Handle, _id, _modifiers, _virtualKey);
        return _isRegistered;
    }

    public new void Dispose()
    {
        if (_isRegistered)
        {
            UnregisterHotKey(Handle, _id);
            _isRegistered = false;
        }

        base.Dispose();
    }

    protected override nint HandleMessage(
        nint hwnd,
        uint message,
        nint wParam,
        nint lParam)
    {
        if (message == WmHotKey && wParam.ToInt32() == _id)
        {
            HotKeyPressed?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        return base.HandleMessage(hwnd, message, wParam, lParam);
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
