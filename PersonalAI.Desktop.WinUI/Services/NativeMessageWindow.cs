using System.Runtime.InteropServices;

namespace PersonalAI.Desktop.WinUI.Services;

public class NativeMessageWindow : IDisposable
{
    private static readonly Dictionary<nint, NativeMessageWindow> Windows = [];
    private static readonly WndProcDelegate SharedWndProc = WndProc;
    private static bool _isClassRegistered;

    private readonly string _className;

    protected NativeMessageWindow(string className)
    {
        _className = className;
        RegisterClassOnce(className);
        Handle = CreateWindowEx(
            0,
            className,
            className,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            GetModuleHandle(null),
            0);

        if (Handle == 0)
        {
            throw new InvalidOperationException("Could not create native message window.");
        }

        Windows[Handle] = this;
    }

    public nint Handle { get; private set; }

    public void Dispose()
    {
        if (Handle != 0)
        {
            Windows.Remove(Handle);
            DestroyWindow(Handle);
            Handle = 0;
        }

        GC.SuppressFinalize(this);
    }

    protected virtual nint HandleMessage(
        nint hwnd,
        uint message,
        nint wParam,
        nint lParam)
    {
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static nint WndProc(
        nint hwnd,
        uint message,
        nint wParam,
        nint lParam)
    {
        return Windows.TryGetValue(hwnd, out var window)
            ? window.HandleMessage(hwnd, message, wParam, lParam)
            : DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static void RegisterClassOnce(string className)
    {
        if (_isClassRegistered)
        {
            return;
        }

        var windowClass = new WindowClass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(SharedWndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = className
        };

        var atom = RegisterClass(ref windowClass);

        if (atom == 0)
        {
            throw new InvalidOperationException("Could not register native window class.");
        }

        _isClassRegistered = true;
    }

    private delegate nint WndProcDelegate(
        nint hwnd,
        uint message,
        nint wParam,
        nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WindowClass lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(
        nint hWnd,
        uint msg,
        nint wParam,
        nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
