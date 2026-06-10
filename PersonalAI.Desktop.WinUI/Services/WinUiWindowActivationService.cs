using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class WinUiWindowActivationService(Window window)
{
    private readonly nint _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);

    public void ShowRestoreAndActivate()
    {
        ShowWindow(_windowHandle, ShowWindowCommand.Restore);
        ShowWindow(_windowHandle, ShowWindowCommand.Show);
        window.Activate();
        SetForegroundWindow(_windowHandle);
    }

    public void Hide()
    {
        ShowWindow(_windowHandle, ShowWindowCommand.Hide);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(nint hWnd, ShowWindowCommand nCmdShow);

    private enum ShowWindowCommand
    {
        Hide = 0,
        Show = 5,
        Restore = 9
    }
}
