using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using PersonalAI.Core.Context;

namespace PersonalAI.Desktop.Windows;

public sealed class ForegroundWindowTracker
{
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly ActiveWindowReferenceTracker _referenceTracker = new();

    public ActiveWindowReference? LastExternalWindow => _referenceTracker.Current;

    public ActiveWindowReference? CaptureCurrentExternalWindow(Window ownWindow)
    {
        var windowHandle = NativeMethods.GetForegroundWindow();

        if (!TryCreateExternalReference(windowHandle, out var reference))
        {
            return _referenceTracker.Current;
        }

        var ownWindowHandle = new WindowInteropHelper(ownWindow).Handle;
        return _referenceTracker.TryRemember(
            reference,
            _ownProcessId,
            ownWindowHandle,
            isWindow: true);
    }

    public ActiveWindowReference? GetLastValidExternalWindow(Window ownWindow)
    {
        var current = _referenceTracker.Current;

        if (current is null)
        {
            return null;
        }

        return _referenceTracker.GetCurrentIfValid(
            NativeMethods.IsWindow(current.WindowHandle));
    }

    private static bool TryCreateExternalReference(
        nint windowHandle,
        out ActiveWindowReference? reference)
    {
        if (windowHandle == 0 || !NativeMethods.IsWindow(windowHandle))
        {
            reference = null;
            return false;
        }

        var processId = NativeMethods.GetProcessId(windowHandle);
        var processName = GetProcessName(processId);
        var windowTitle = NativeMethods.GetWindowTitle(windowHandle);

        reference = new ActiveWindowReference(
            windowHandle,
            processId,
            processName,
            windowTitle,
            DateTimeOffset.UtcNow);
        return true;
    }

    private static string? GetProcessName(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (
            exception is ArgumentException ||
            exception is InvalidOperationException)
        {
            Debug.WriteLine(
                $"PersonalAI active-window process lookup failed: {exception.Message}");
            return null;
        }
    }

    private static class NativeMethods
    {
        public static uint GetProcessId(nint windowHandle)
        {
            _ = GetWindowThreadProcessId(windowHandle, out var processId);
            return processId;
        }

        public static string? GetWindowTitle(nint windowHandle)
        {
            var length = GetWindowTextLength(windowHandle);

            if (length <= 0)
            {
                return null;
            }

            var builder = new StringBuilder(length + 1);
            var copied = GetWindowText(windowHandle, builder, builder.Capacity);

            return copied > 0 ? builder.ToString() : null;
        }

        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool IsWindow(nint hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(
            nint hWnd,
            out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowTextLength(nint hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(
            nint hWnd,
            StringBuilder lpString,
            int nMaxCount);
    }
}
