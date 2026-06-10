using System.Runtime.InteropServices;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class WinUiTrayIconService : NativeMessageWindow
{
    private const uint TrayIconId = 1;
    private const uint WmTrayIcon = 0x8000 + 1;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint MfString = 0x00000000;
    private const uint TpmReturNCmd = 0x0100;
    private const uint TpmNonotify = 0x0080;
    private const int CommandOpen = 1001;
    private const int CommandNewChat = 1002;
    private const int CommandExit = 1003;
    private readonly Action _openPersonalAi;
    private readonly Action _newChat;
    private readonly Action _exit;
    private bool _isAdded;

    public WinUiTrayIconService(
        Action openPersonalAi,
        Action newChat,
        Action exit)
        : base("PersonalAI.WinUI.MessageWindow")
    {
        _openPersonalAi = openPersonalAi;
        _newChat = newChat;
        _exit = exit;
        AddIcon();
    }

    public new void Dispose()
    {
        RemoveIcon();
        base.Dispose();
    }

    protected override nint HandleMessage(
        nint hwnd,
        uint message,
        nint wParam,
        nint lParam)
    {
        if (message == WmTrayIcon)
        {
            var mouseMessage = (uint)lParam.ToInt64();

            if (mouseMessage is WmLButtonUp or WmLButtonDblClk)
            {
                _openPersonalAi();
                return 0;
            }

            if (mouseMessage == WmRButtonUp)
            {
                ShowContextMenu();
                return 0;
            }
        }

        return base.HandleMessage(hwnd, message, wParam, lParam);
    }

    private void AddIcon()
    {
        var data = CreateNotifyIconData();
        data.uFlags = NifMessage | NifIcon | NifTip;
        data.uCallbackMessage = WmTrayIcon;
        data.hIcon = LoadIcon(0, new IntPtr(32512));
        data.szTip = "PersonalAI";
        _isAdded = Shell_NotifyIcon(NimAdd, ref data);
    }

    private void RemoveIcon()
    {
        if (!_isAdded)
        {
            return;
        }

        var data = CreateNotifyIconData();
        Shell_NotifyIcon(NimDelete, ref data);
        _isAdded = false;
    }

    private NotifyIconData CreateNotifyIconData()
    {
        return new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = Handle,
            uID = TrayIconId
        };
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();

        try
        {
            AppendMenu(menu, MfString, CommandOpen, "Open PersonalAI");
            AppendMenu(menu, MfString, CommandNewChat, "New Chat");
            AppendMenu(menu, MfString, CommandExit, "Exit");
            GetCursorPos(out var point);
            SetForegroundWindow(Handle);
            var command = TrackPopupMenu(
                menu,
                TpmReturNCmd | TpmNonotify,
                point.X,
                point.Y,
                0,
                Handle,
                0);

            switch (command)
            {
                case CommandOpen:
                    _openPersonalAi();
                    break;
                case CommandNewChat:
                    _newChat();
                    break;
                case CommandExit:
                    _exit();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(
        uint dwMessage,
        ref NotifyIconData lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadIcon(nint hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(
        nint hMenu,
        uint uFlags,
        int uIDNewItem,
        string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenu(
        nint hMenu,
        uint uFlags,
        int x,
        int y,
        int nReserved,
        nint hWnd,
        nint prcRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(nint hWnd);
}
