using System.Runtime.InteropServices;

namespace PersonalAI.Desktop.Avalonia.Platform.Windows.Assist;

public interface IAvaloniaAssistWindowNative
{
    nint ForegroundWindow { get; }

    bool IsWindow(nint window);

    uint GetProcessId(nint window);

    void SetNoActivate(nint window, bool enabled);

    bool ShowNoActivate(nint window);

    bool RestoreAndActivate(nint window);

    bool SetForegroundWindow(nint window);
}

public sealed class AvaloniaAssistWindowIntegration(
    Func<nint> getAssistWindowHandle,
    IAvaloniaAssistWindowNative? native = null)
{
    private readonly IAvaloniaAssistWindowNative _native = native ?? new Win32AssistWindowNative();
    private FocusReturnTarget? _focusReturnTarget;

    public void CaptureFocusReturnTarget()
    {
        var assist = getAssistWindowHandle();
        var foreground = _native.ForegroundWindow;
        var processId = _native.GetProcessId(foreground);
        if (foreground != 0 &&
            foreground != assist &&
            _native.IsWindow(foreground) &&
            processId != 0 &&
            processId != Environment.ProcessId)
        {
            _focusReturnTarget = new FocusReturnTarget(foreground, processId);
        }
    }

    public bool ShowIdleWithoutActivation()
    {
        var assist = getAssistWindowHandle();
        if (assist == 0 || !_native.IsWindow(assist))
        {
            return false;
        }

        _native.SetNoActivate(assist, true);
        return _native.ShowNoActivate(assist);
    }

    public bool ActivatePrompt()
    {
        CaptureFocusReturnTarget();
        var assist = getAssistWindowHandle();
        if (assist == 0 || !_native.IsWindow(assist))
        {
            return false;
        }

        _native.SetNoActivate(assist, false);
        return _native.RestoreAndActivate(assist);
    }

    public bool RestoreFocus()
    {
        if (_focusReturnTarget is not { } target ||
            !_native.IsWindow(target.WindowHandle) ||
            _native.GetProcessId(target.WindowHandle) != target.ProcessId)
        {
            return false;
        }

        return _native.SetForegroundWindow(target.WindowHandle);
    }

    private readonly record struct FocusReturnTarget(nint WindowHandle, uint ProcessId);

    private sealed class Win32AssistWindowNative : IAvaloniaAssistWindowNative
    {
        private const int ExtendedStyleIndex = -20;
        private const long NoActivate = 0x08000000;

        public nint ForegroundWindow => GetForegroundWindow();

        public bool IsWindow(nint window) => IsWindowNative(window);

        public uint GetProcessId(nint window)
        {
            _ = GetWindowThreadProcessId(window, out var processId);
            return processId;
        }

        public void SetNoActivate(nint window, bool enabled)
        {
            var style = GetWindowLongPtr(window, ExtendedStyleIndex).ToInt64();
            style = enabled ? style | NoActivate : style & ~NoActivate;
            _ = SetWindowLongPtr(window, ExtendedStyleIndex, new nint(style));
        }

        public bool ShowNoActivate(nint window) =>
            ShowWindow(window, ShowWindowCommand.ShowNoActivate);

        public bool RestoreAndActivate(nint window) =>
            RestoreAndActivateWindow(window);

        public bool SetForegroundWindow(nint window) =>
            SetForegroundWindowNative(window);

        private static bool RestoreAndActivateWindow(nint window)
        {
            _ = ShowWindow(window, ShowWindowCommand.Restore);
            return SetForegroundWindowNative(window);
        }

        [DllImport("user32.dll")]
        private static extern nint GetForegroundWindow();

        [DllImport("user32.dll", EntryPoint = "IsWindow")]
        private static extern bool IsWindowNative(nint window);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern nint GetWindowLongPtr(nint window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetWindowLongPtr(nint window, int index, nint value);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(nint window, ShowWindowCommand command);

        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        private static extern bool SetForegroundWindowNative(nint window);

        private enum ShowWindowCommand
        {
            ShowNoActivate = 4,
            Restore = 9
        }
    }
}
