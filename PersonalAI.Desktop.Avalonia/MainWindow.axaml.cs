using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using PersonalAI.Desktop.Avalonia.Composition;
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;
using PersonalAI.Desktop.Avalonia.Views.Chat;

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
    private static readonly IReadOnlyDictionary<string, string> NavigationIcons =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["home"] = "M3,11 L12,4 L21,11 M5,10 L5,20 L10,20 L10,14 L14,14 L14,20 L19,20 L19,10",
            ["chat"] = "M7,5 L17,5 C19.2,5 21,6.8 21,9 L21,13 C21,15.2 19.2,17 17,17 L11,17 L6,21 L7,17 C4.8,17 3,15.2 3,13 L3,9 C3,6.8 4.8,5 7,5 Z",
            ["aeda-code"] = "M9,6 L3,12 L9,18 M15,6 L21,12 L15,18 M14,3 L10,21",
            ["aeda-memory"] = "M7,4 L17,4 C19.2,4 21,5.8 21,8 L21,16 C21,18.2 19.2,20 17,20 L7,20 C4.8,20 3,18.2 3,16 L3,8 C3,5.8 4.8,4 7,4 Z M8,10 L8,10.1 M12,10 L12,10.1 M16,10 L16,10.1 M8,14 L16,14",
            ["aeda-research"] = "M3,7 L3,19 C3,20.1 3.9,21 5,21 L19,21 C20.1,21 21,20.1 21,19 L21,9 C21,7.9 20.1,7 19,7 L12,7 L10,4 L5,4 C3.9,4 3,4.9 3,6 Z",
            ["aeda-task-center"] = "M4,5 L8,5 L8,9 L4,9 Z M11,7 L21,7 M4,12 L8,12 L8,16 L4,16 Z M11,14 L21,14 M4,19 L8,19 L8,21 L4,21 M11,20 L18,20",
            ["aeda-assist"] = "M12,3 C13.1,3 14,3.6 14.6,4.6 L22,18 C23.1,20 21.7,22 19.5,22 L4.5,22 C2.3,22 0.9,20 2,18 L9.4,4.6 C10,3.6 10.9,3 12,3 Z M12,9 L12,15 M9.5,12 L14.5,12",
            ["settings"] = "M12,8.5 A3.5,3.5 0 1 0 12,15.5 A3.5,3.5 0 1 0 12,8.5 M12,3 L12,5 M12,19 L12,21 M3,12 L5,12 M19,12 L21,12 M5.6,5.6 L7,7 M17,17 L18.4,18.4 M18.4,5.6 L17,7 M7,17 L5.6,18.4",
            ["module"] = "M4,4 L10,4 L10,10 L4,10 Z M14,4 L20,4 L20,10 L14,10 Z M4,14 L10,14 L10,20 L4,20 Z M14,14 L20,14 L20,20 L14,20 Z"
        };

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
            Data = Geometry.Parse(NavigationIcons.GetValueOrDefault(route, NavigationIcons["module"])),
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
