using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace PersonalAI.Desktop.WinUI.Services;

public static class AedaWindowChrome
{
    public const string AppUserModelId = "AEDA.LocalIntelligence";

    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;
    private const uint WmSetIcon = 0x0080;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int SystemMetricIconWidth = 11;
    private const int SystemMetricIconHeight = 12;
    private const int SystemMetricSmallIconWidth = 49;
    private const int SystemMetricSmallIconHeight = 50;
    private const int UseImmersiveDarkMode = 20;
    private const int WindowCornerPreference = 33;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;
    private static nint _largeIcon;
    private static nint _smallIcon;

    public static void InitializeProcessIdentity()
    {
        var result = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    public static void Apply(AppWindow appWindow, nint windowHandle, bool isDark)
    {
        var resources = Application.Current.Resources;
        Set(windowHandle, UseImmersiveDarkMode, isDark ? 1 : 0);
        Set(windowHandle, WindowCornerPreference, 2);
        Set(windowHandle, BorderColor, ToColorRef((Color)resources["AedaWindowBorderColor"]));
        Set(windowHandle, CaptionColor, ToColorRef((Color)resources["AedaTitleBarBackgroundColor"]));
        Set(windowHandle, TextColor, ToColorRef((Color)resources["AedaTitleBarForegroundColor"]));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AedaAppIcon.ico");
        if (File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
            SetWindowIcons(windowHandle, iconPath);
        }
    }

    internal static int ToColorRef(Color color) =>
        color.R | color.G << 8 | color.B << 16;

    private static void Set(nint windowHandle, int attribute, int value)
    {
        try
        {
            _ = DwmSetWindowAttribute(windowHandle, attribute, ref value, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static void SetWindowIcons(nint windowHandle, string iconPath)
    {
        // WM_SETICON retains these handles, so keep them alive for the process lifetime.
        _largeIcon = LoadIcon(
            _largeIcon,
            iconPath,
            GetSystemMetrics(SystemMetricIconWidth),
            GetSystemMetrics(SystemMetricIconHeight));
        _smallIcon = LoadIcon(
            _smallIcon,
            iconPath,
            GetSystemMetrics(SystemMetricSmallIconWidth),
            GetSystemMetrics(SystemMetricSmallIconHeight));

        if (_largeIcon != 0)
        {
            _ = SendMessage(windowHandle, WmSetIcon, IconBig, _largeIcon);
        }

        if (_smallIcon != 0)
        {
            _ = SendMessage(windowHandle, WmSetIcon, IconSmall, _smallIcon);
        }
    }

    private static nint LoadIcon(nint current, string iconPath, int width, int height) =>
        current != 0
            ? current
            : LoadImage(0, iconPath, ImageIcon, width, height, LoadFromFile);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appUserModelId);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(
        nint instance,
        string name,
        uint type,
        int width,
        int height,
        uint loadFlags);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam);
}
