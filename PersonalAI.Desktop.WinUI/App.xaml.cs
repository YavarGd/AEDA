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

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var chatProvider = ChatProviderFactory.CreateDefaultLocalProvider();
        var conversationRepository = ConversationRepositoryFactory.CreateDefaultRepository();
        await conversationRepository.InitializeAsync();
        var chatSession = new ChatSessionService(chatProvider);
        var conversationSession = new ConversationSessionService(
            conversationRepository,
            chatSession);
        var viewModel = new MainViewModel(conversationSession);
        await viewModel.InitializeAsync();

        _window = new MainWindow(viewModel);
        _window.Activate();
    }
}
