using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using PersonalAI.Desktop.Avalonia.Composition;
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;
using PersonalAI.Desktop.Avalonia.Views.Chat;
using PersonalAI.Desktop.Avalonia.Views.Code;
using PersonalAI.Desktop.Avalonia.Views.Memory;
using PersonalAI.Desktop.Avalonia.Views.Research;
using PersonalAI.Desktop.Avalonia.Views.Tasks;

namespace PersonalAI.Desktop.Avalonia;

/// <summary>The shell routes available in this build.</summary>
public enum ShellRoute
{
    Dashboard,
    Chat,
    Presentation
}

public partial class MainWindow : Window
{
    private readonly Dictionary<ListBoxItem, MountedPresentationScreen> _screenByNavItem = [];
    private readonly Dictionary<string, MountedPresentationScreen> _screenByRoute =
        new(StringComparer.Ordinal);
    private bool _suppressNavigationSelection;
    private MountedPresentationScreen? _activePresentationScreen;

    public MainWindow()
    {
        InitializeComponent();

        DashboardNavItem.Content = CreateNavigationContent("Home", "home");
        ChatNavItem.Content = CreateNavigationContent("Chat", "chat");

        DashboardRoute.OpenChatRequested += (_, _) =>
            Navigate(ShellRoute.Chat, focusContent: true);
        DashboardRoute.OpenModuleRequested += (_, e) =>
            NavigateToPresentation(e.Route, focusContent: true);
        Opened += (_, _) =>
            Navigate(ShellRoute.Dashboard, focusContent: true);
        SizeChanged += (_, _) => ApplyResponsiveMode();
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
        ApplyResponsiveMode();
    }

    /// <summary>The route currently shown. Shell state stays in the view.</summary>
    public ShellRoute CurrentRoute { get; private set; } = ShellRoute.Dashboard;

    public void AttachComposition(
        AvaloniaChatViewModel chat,
        IReadOnlyList<AvaloniaPresentationScreen> screens)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(screens);
        if (_screenByRoute.Count > 0)
        {
            throw new InvalidOperationException("Presentation composition is already attached.");
        }

        DataContext = chat;
        screens = screens.OrderBy(ScreenOrder).ToArray();
        foreach (var screen in screens)
        {
            if (string.IsNullOrWhiteSpace(screen.Route) ||
                string.IsNullOrWhiteSpace(screen.Label))
            {
                throw new ArgumentException("Presentation screens require a route and label.");
            }

            var content = screen.CreateContent();
            content.DataContext = screen.ViewModel;
            var navItem = new ListBoxItem
            {
                Content = CreateNavigationContent(screen.Label, screen.Route)
            };
            navItem.Classes.Set("lowFrequency", screen.Route == "settings");
            AutomationProperties.SetName(navItem, screen.Label);
            var mounted = new MountedPresentationScreen(screen, content, navItem);
            if (!_screenByRoute.TryAdd(screen.Route, mounted))
            {
                throw new ArgumentException($"Duplicate presentation route: {screen.Route}");
            }

            _screenByNavItem.Add(navItem, mounted);
            NavigationList.Items.Add(navItem);
        }
    }

    public bool NavigateToPresentation(string route, bool focusContent = true)
    {
        if (!_screenByRoute.TryGetValue(route, out var screen))
        {
            return false;
        }

        _activePresentationScreen = screen;
        Navigate(ShellRoute.Presentation, focusContent);
        return true;
    }

    public void OpenChat(bool newChat)
    {
        if (newChat && DataContext is AvaloniaChatViewModel chat)
        {
            chat.NewChat();
        }

        Navigate(ShellRoute.Chat, focusContent: true);
    }

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
        PresentationRoute.IsVisible = route == ShellRoute.Presentation;
        PresentationRoute.Content = route == ShellRoute.Presentation
            ? _activePresentationScreen?.Content
            : null;
        if (route == ShellRoute.Presentation)
        {
            ApplyResponsiveMode();
        }

        PageTitle.Text = route switch
        {
            ShellRoute.Chat => "General Chat",
            ShellRoute.Presentation => _activePresentationScreen?.Definition.Label ?? "AEDA",
            _ => "Home"
        };

        var navItem = route switch
        {
            ShellRoute.Chat => ChatNavItem,
            ShellRoute.Presentation => _activePresentationScreen?.NavItem,
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
        else if (route == ShellRoute.Presentation &&
            _activePresentationScreen is { } presentation)
        {
            presentation.Definition.Focus?.Invoke(presentation.Content);
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

        if (NavigationList.SelectedItem is ListBoxItem item &&
            _screenByNavItem.TryGetValue(item, out var screen))
        {
            _activePresentationScreen = screen;
            Navigate(ShellRoute.Presentation, focusContent: false);
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

    private void ApplyResponsiveMode()
    {
        var compact = ClientSize.Width < 760;
        var medium = ClientSize.Width is >= 760 and < 1000;
        Classes.Set("compact", compact);
        Classes.Set("medium", medium);
        DashboardRoute.ApplyResponsiveMode(compact, medium);
        ChatRoute.ApplyResponsiveMode(compact, medium);
        if (_activePresentationScreen?.Content is CodeView codeRoute)
        {
            codeRoute.ApplyResponsiveMode(compact, medium);
        }

        if (_activePresentationScreen?.Content is MemoryView memoryRoute)
        {
            memoryRoute.ApplyResponsiveMode(compact, medium);
        }

        if (_activePresentationScreen?.Content is ResearchView researchRoute)
        {
            researchRoute.ApplyResponsiveMode(compact, medium);
        }

        if (_activePresentationScreen?.Content is TaskCenterView taskCenterRoute)
        {
            taskCenterRoute.ApplyResponsiveMode(compact, medium);
        }

        ShellGrid.RowDefinitions[0].Height = new GridLength(compact ? 56 : 64);
        ShellGrid.RowDefinitions[2].Height = new GridLength(compact ? 56 : medium ? 64 : 72);
        TopBarLayout.Margin = new Thickness(compact ? 16 : medium ? 24 : 32, 0);
        BrandMark.Width = BrandMark.Height = compact ? 20 : 24;
        BrandWordmark.FontSize = compact ? 15 : medium ? 16 : 17;
        BreadcrumbBrand.IsVisible = !compact;
        BreadcrumbSeparator.IsVisible = !compact;
    }

    private static Control CreateNavigationContent(string label, string route)
    {
        var icon = new global::Avalonia.Controls.Shapes.Path
        {
            Data = RouteIconCatalog.Get(route),
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Classes = { "navIcon" }
        };
        var text = new TextBlock
        {
            Text = label,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Classes = { "navLabel" }
        };
        return new StackPanel
        {
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            Classes = { "navContent" },
            Children = { icon, text }
        };
    }

    private static int ScreenOrder(AvaloniaPresentationScreen screen) => screen.Route switch
    {
        "aeda-code" => 0,
        "aeda-memory" => 1,
        "aeda-research" => 2,
        "aeda-task-center" => 3,
        "aeda-assist" => 4,
        "settings" => 5,
        _ => 6
    };

    private sealed record MountedPresentationScreen(
        AvaloniaPresentationScreen Definition,
        Control Content,
        ListBoxItem NavItem);
}
