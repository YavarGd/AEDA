using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PersonalAI.Core.Tasks;
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;
using PersonalAI.Desktop.Avalonia.Views.Chat;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Desktop.Avalonia;

/// <summary>The shell routes available in this build.</summary>
public enum ShellRoute
{
    Dashboard,
    Chat,
    TaskCenter
}

public partial class MainWindow : Window
{
    private bool _suppressNavigationSelection;

    public MainWindow()
    {
        InitializeComponent();

        DashboardRoute.OpenChatRequested += (_, _) =>
            Navigate(ShellRoute.Chat, focusContent: true);
        Opened += (_, _) =>
            Navigate(ShellRoute.Dashboard, focusContent: true);
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>The route currently shown. Shell state stays in the view.</summary>
    public ShellRoute CurrentRoute { get; private set; } = ShellRoute.Dashboard;

    public void AttachTaskCenter(IAedaTaskCenterService taskCenterService) =>
        TaskCenterRoute.DataContext = new AedaTaskCenterViewModel(taskCenterService);

    /// <summary>
    /// Routes the shell. <paramref name="focusContent"/> must stay false for
    /// selection-driven routing so arrow-keying the navigation list does not eject focus
    /// into page content; explicit routing passes true.
    /// </summary>
    private void Navigate(ShellRoute route, bool focusContent)
    {
        CurrentRoute = route;
        DashboardRoute.IsVisible = route == ShellRoute.Dashboard;
        ChatRoute.IsVisible = route == ShellRoute.Chat;
        TaskCenterRoute.IsVisible = route == ShellRoute.TaskCenter;

        var navItem = route switch
        {
            ShellRoute.Chat => ChatNavItem,
            ShellRoute.TaskCenter => TaskCenterNavItem,
            _ => DashboardNavItem
        };
        if (!ReferenceEquals(NavigationList.SelectedItem, navItem))
        {
            _suppressNavigationSelection = true;
            try
            {
                NavigationList.SelectedItem = navItem;
            }
            finally
            {
                _suppressNavigationSelection = false;
            }
        }

        if (!focusContent)
        {
            return;
        }

        if (route == ShellRoute.Chat)
        {
            ChatRoute.FocusComposer();
        }
        else if (route == ShellRoute.TaskCenter)
        {
            TaskCenterRoute.FocusPrimaryAction();
        }
        else
        {
            DashboardRoute.FocusPrimaryAction();
        }
    }

    private void OnNavigationSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressNavigationSelection)
        {
            return;
        }

        if (ReferenceEquals(NavigationList.SelectedItem, ChatNavItem))
        {
            Navigate(ShellRoute.Chat, focusContent: false);
            return;
        }

        if (ReferenceEquals(NavigationList.SelectedItem, DashboardNavItem))
        {
            Navigate(ShellRoute.Dashboard, focusContent: false);
            return;
        }

        if (ReferenceEquals(NavigationList.SelectedItem, TaskCenterNavItem))
        {
            Navigate(ShellRoute.TaskCenter, focusContent: false);
        }
    }

    /// <summary>
    /// Escape cancels only while generating and Ctrl+N starts a chat. Both are routed to
    /// the chat view so the shell holds no chat state.
    /// </summary>
    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        var isGenerating = (DataContext as AvaloniaChatViewModel)?.IsGenerating ?? false;
        var action = ChatKeyboardPolicy.ResolveShellKey(e.Key, e.KeyModifiers, isGenerating);

        if (action == ChatKeyAction.None)
        {
            return;
        }

        if (action == ChatKeyAction.NewChat)
        {
            Navigate(ShellRoute.Chat, focusContent: true);
        }

        ChatRoute.ApplyKeyAction(action);
        e.Handled = true;
    }
}
