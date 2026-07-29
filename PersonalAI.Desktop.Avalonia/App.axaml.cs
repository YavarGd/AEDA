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

    public override async void OnFrameworkInitializationCompleted()
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

            _composition = await AvaloniaAppComposition.CreateAsync(
                action => global::Avalonia.Threading.Dispatcher.UIThread.Post(action));
            await _composition.Chat.InitializeAsync();
            var window = new MainWindow();
            window.DataContext = _composition.Chat;
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => DisposeComposition();
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

    private void DisposeComposition()
    {
        _composition?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _composition = null;
        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
    }
}
