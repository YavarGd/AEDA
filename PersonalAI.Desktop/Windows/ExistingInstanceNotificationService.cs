using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PersonalAI.Desktop.Windows;

public sealed class ExistingInstanceNotificationService : IDisposable
{
    public const string ShowPaletteMessageName = "PersonalAI.ShowPalette";

    private const int HwndBroadcast = 0xffff;

    private readonly HwndSource _source;
    private readonly int _showPaletteMessage;

    public event EventHandler? ShowPaletteRequested;

    public ExistingInstanceNotificationService(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            throw new ArgumentException(
                "A valid window handle is required.",
                nameof(windowHandle));
        }

        _showPaletteMessage = RegisterWindowMessage(ShowPaletteMessageName);
        _source = HwndSource.FromHwnd(windowHandle) ??
            throw new InvalidOperationException(
                "Could not create a WPF message source for the window.");
        _source.AddHook(WndProc);
    }

    public static bool NotifyExistingInstance()
    {
        var message = RegisterWindowMessage(ShowPaletteMessageName);

        if (message == 0)
        {
            return false;
        }

        return PostMessage(HwndBroadcast, message, 0, 0);
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
    }

    private nint WndProc(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == _showPaletteMessage)
        {
            ShowPaletteRequested?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(
        int hWnd,
        int msg,
        nint wParam,
        nint lParam);
}
