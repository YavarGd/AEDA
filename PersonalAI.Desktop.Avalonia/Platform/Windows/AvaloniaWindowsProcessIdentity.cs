using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace PersonalAI.Desktop.Avalonia.Platform.Windows;

public readonly record struct AvaloniaWindowProcessIdentity(
    nint WindowHandle,
    uint ProcessId);

public static class AvaloniaWindowsProcessIdentity
{
    public const string AppUserModelId = "AEDA.LocalIntelligence";

    public static void InitializeProcessIdentity()
    {
        var result = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    public static bool TryGetWindowIdentity(
        TopLevel topLevel,
        out AvaloniaWindowProcessIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        var platformHandle = topLevel.TryGetPlatformHandle();
        var windowHandle = platformHandle?.Handle ?? 0;
        if (windowHandle == 0 ||
            !string.Equals(platformHandle?.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase))
        {
            identity = default;
            return false;
        }

        _ = GetWindowThreadProcessId(windowHandle, out var processId);
        identity = new AvaloniaWindowProcessIdentity(windowHandle, processId);
        return processId != 0;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appUserModelId);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
