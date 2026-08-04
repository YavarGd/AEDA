using Avalonia;
using Avalonia.Controls;
using PersonalAI.Desktop.Avalonia.Platform.Windows;
using PersonalAI.Infrastructure.Windows;

namespace PersonalAI.Desktop.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var singleInstance = new WindowsSingleInstanceService();
        if (!singleInstance.IsPrimaryInstance)
        {
            return 0;
        }

        AvaloniaWindowsProcessIdentity.InitializeProcessIdentity();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(
            args,
            ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();
}
