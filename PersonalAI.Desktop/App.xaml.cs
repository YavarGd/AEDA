using System.Windows;
using PersonalAI.Infrastructure.Chat;

namespace PersonalAI.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var chatProvider = ChatProviderFactory.CreateDefaultLocalProvider();
        var conversationRepository =
            ConversationRepositoryFactory.CreateDefaultRepository();

        try
        {
            await conversationRepository.InitializeAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"PersonalAI could not initialize the local conversation database. {exception.Message}",
                "PersonalAI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
            return;
        }

        var mainWindow = new MainWindow(chatProvider, conversationRepository);
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
