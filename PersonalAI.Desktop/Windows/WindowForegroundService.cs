using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PersonalAI.Desktop.Windows;

public static class WindowForegroundService
{
    public static void BringToForeground(Window window)
    {
        window.Activate();

        var handle = new WindowInteropHelper(window).Handle;

        if (handle != 0)
        {
            SetForegroundWindow(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(nint hWnd);
}
