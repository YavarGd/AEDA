#if WINDOWS
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using PersonalAI.Core.Context;

namespace PersonalAI.Infrastructure.Context;

public sealed class WindowsActiveContextProvider(
    TemporaryScreenshotStore screenshotStore) : IActiveContextProvider
{
    public Task<ActiveApplicationContext?> CaptureAsync(
        ContextCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var windowHandle = request.WindowHandle ?? NativeMethods.GetForegroundWindow();

        if (windowHandle == 0 || !NativeMethods.IsWindow(windowHandle))
        {
            return Task.FromResult<ActiveApplicationContext?>(null);
        }

        var processId = NativeMethods.GetProcessId(windowHandle);
        var processInfo = GetProcessInfo(processId);
        var windowTitle = NativeMethods.GetWindowTitle(windowHandle);
        string? screenshotPath = null;

        if (request.CaptureScreenshot)
        {
            screenshotPath = CaptureWindowScreenshot(windowHandle);
        }

        return Task.FromResult<ActiveApplicationContext?>(new ActiveApplicationContext(
            windowHandle,
            processId,
            processInfo.ProcessName,
            processInfo.ExecutablePath,
            windowTitle,
            request.SelectedText,
            screenshotPath,
            ScreenshotBytes: null,
            DateTimeOffset.UtcNow));
    }

    private string? CaptureWindowScreenshot(nint windowHandle)
    {
        if (!NativeMethods.GetWindowRect(windowHandle, out var rect))
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        var deviceContext = graphics.GetHdc();

        try
        {
            if (!NativeMethods.PrintWindow(windowHandle, deviceContext, 0))
            {
                return null;
            }
        }
        finally
        {
            graphics.ReleaseHdc(deviceContext);
        }

        return screenshotStore.Save(bitmap);
    }

    private static ProcessInfo GetProcessInfo(uint processId)
    {
        if (processId == 0)
        {
            return new ProcessInfo(null, null);
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            string? executablePath = null;

            try
            {
                executablePath = process.MainModule?.FileName;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException ||
                exception is NotSupportedException ||
                exception is System.ComponentModel.Win32Exception)
            {
                executablePath = null;
            }

            return new ProcessInfo(process.ProcessName, executablePath);
        }
        catch (Exception exception) when (
            exception is ArgumentException ||
            exception is InvalidOperationException)
        {
            return new ProcessInfo(null, null);
        }
    }

    private sealed record ProcessInfo(
        string? ProcessName,
        string? ExecutablePath);

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
        public static extern bool GetWindowRect(nint hWnd, out WindowRect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PrintWindow(
            nint hwnd,
            nint hdcBlt,
            uint nFlags);

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

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct WindowRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
#endif
