using Avalonia;
using Avalonia.Controls;
using PersonalAI.Desktop.Avalonia.Platform.Windows;

namespace PersonalAI.Desktop.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AvaloniaWindowsProcessIdentity.InitializeProcessIdentity();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(
            args,
            ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();
}
