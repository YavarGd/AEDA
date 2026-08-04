using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace PersonalAI.Desktop.Avalonia.Platform.Windows;

public sealed class AvaloniaWindowActivationService(Window window)
{
    public bool ShowRestoreAndActivate()
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        if (window.IsActive)
        {
            return true;
        }

        if (!AvaloniaWindowsProcessIdentity.TryGetWindowIdentity(window, out var identity))
        {
            return false;
        }

        _ = ShowWindow(identity.WindowHandle, ShowWindowCommand.Restore);
        return SetForegroundWindow(identity.WindowHandle);
    }

    public void Hide() => window.Hide();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(nint window, ShowWindowCommand command);

    private enum ShowWindowCommand
    {
        Restore = 9
    }
}
