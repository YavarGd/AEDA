using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace PersonalAI.Desktop.WinUI.Services;

public static class AedaWindowChrome
{
    private const int UseImmersiveDarkMode = 20;
    private const int WindowCornerPreference = 33;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int value,
        int valueSize);
}
