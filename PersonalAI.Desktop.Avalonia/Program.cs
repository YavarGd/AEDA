using Avalonia;
using Avalonia.Controls;
using PersonalAI.Desktop.Avalonia.Platform.Windows;
using PersonalAI.Infrastructure.Ipc;
using PersonalAI.Infrastructure.Windows;

namespace PersonalAI.Desktop.Avalonia;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        using var singleInstance = new WindowsSingleInstanceService();
        if (!singleInstance.IsPrimaryInstance)
        {
            return await PersonalAiActivationClient.TryActivatePrimaryAsync()
                .ConfigureAwait(false)
                ? 0
                : 1;
        }

        AvaloniaWindowsProcessIdentity.InitializeProcessIdentity();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(
            args,
            ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();
}
