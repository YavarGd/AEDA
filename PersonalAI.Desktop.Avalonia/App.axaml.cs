using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PersonalAI.Desktop.Avalonia.Composition;
using PersonalAI.Infrastructure.Windows;

namespace PersonalAI.Desktop.Avalonia;

public partial class App : Application
{
    private WindowsSingleInstanceService? _singleInstanceService;
    private AvaloniaAppComposition? _composition;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        try
        {
            _singleInstanceService = new WindowsSingleInstanceService();
            if (!_singleInstanceService.IsPrimaryInstance)
            {
                _singleInstanceService.Dispose();
                _singleInstanceService = null;
                desktop.Shutdown();
                return;
            }

            var window = new MainWindow();
            desktop.Exit += (_, _) => DisposeComposition();
            _ = InitializeCompositionAsync(desktop, window);
        }
        catch
        {
            DisposeComposition();
            desktop.Shutdown(1);
        }
        finally
        {
            base.OnFrameworkInitializationCompleted();
        }
    }

    private async Task InitializeCompositionAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow window)
    {
        try
        {
            _composition = await AvaloniaAppComposition.CreateAsync(
                action => global::Avalonia.Threading.Dispatcher.UIThread.Post(action));
            await _composition.Chat.InitializeAsync();
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    window.DataContext = _composition.Chat;
                    desktop.MainWindow = window;
                    window.Show();
                });
        }
        catch
        {
            DisposeComposition();
            desktop.Shutdown(1);
        }
    }

    private void DisposeComposition()
    {
        _composition?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _composition = null;
        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
    }
}
