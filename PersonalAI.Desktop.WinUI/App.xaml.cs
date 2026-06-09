using Microsoft.UI.Xaml;
using PersonalAI.Core.Chat;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Desktop.WinUI.ViewModels;
using PersonalAI.Desktop.WinUI.Views;
using PersonalAI.Infrastructure.Chat;

namespace PersonalAI.Desktop.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var chatProvider = ChatProviderFactory.CreateDefaultLocalProvider();
        var chatSession = new ChatSessionService(chatProvider);
        var viewModel = new MainViewModel(chatSession);

        _window = new MainWindow(viewModel);
        _window.Activate();
    }
}
