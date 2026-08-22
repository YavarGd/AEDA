using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;
using PersonalAI.Desktop.Avalonia.Views.Chat;

namespace PersonalAI.Desktop.Avalonia.Views.Dashboard;

public sealed class DashboardModuleRequestedEventArgs(string route) : EventArgs
{
    public string Route { get; } = route;
}

public partial class DashboardView : UserControl
{
    private AvaloniaChatViewModel? _viewModel;
    private readonly Button[] _capabilityCards;

    public DashboardView()
    {
        InitializeComponent();
        _capabilityCards =
            [ChatCard, CodeCard, MemoryCard, ResearchCard, TaskCenterCard, AssistCard];
        StartConversationIcon.Data = RouteIconCatalog.Get("chat");
        ChatIcon.Data = RouteIconCatalog.Get("chat");
        CodeIcon.Data = RouteIconCatalog.Get("aeda-code");
        MemoryIcon.Data = RouteIconCatalog.Get("aeda-memory");
        ResearchIcon.Data = RouteIconCatalog.Get("aeda-research");
        TaskCenterIcon.Data = RouteIconCatalog.Get("aeda-task-center");
        AssistIcon.Data = RouteIconCatalog.Get("aeda-assist");
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Raised when the reader asks to go to chat. The shell owns navigation, so the
    /// dashboard only reports the intent.
    /// </summary>
    public event EventHandler? OpenChatRequested;

    public event EventHandler<DashboardModuleRequestedEventArgs>? OpenModuleRequested;

    public void FocusPrimaryAction() => StartConversationButton.Focus();

    public void ApplyResponsiveMode(bool compact, bool medium)
    {
        var layout = ResolveLayout(compact, medium);
        ContentPanel.MaxWidth = layout.MaxWidth;
        ContentPanel.Margin = new Thickness(layout.PagePadding);
        ContentPanel.Spacing = layout.SectionGap;
        WelcomeHeadline.FontSize = layout.HeadingSize;
        StartConversationButton.MinHeight = layout.PrimaryHeight;
        StatusStrip.Padding = new Thickness(
            layout.PagePadding,
            compact ? 8 : medium ? 9 : 10);

        CapabilityGrid.ColumnSpacing = layout.CardGap;
        CapabilityGrid.RowSpacing = layout.CardGap;
        CapabilityGrid.ColumnDefinitions.Clear();
        CapabilityGrid.RowDefinitions.Clear();
        for (var column = 0; column < layout.Columns; column++)
        {
            CapabilityGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        var rows = (_capabilityCards.Length + layout.Columns - 1) / layout.Columns;
        for (var row = 0; row < rows; row++)
        {
            CapabilityGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var index = 0; index < _capabilityCards.Length; index++)
        {
            var card = _capabilityCards[index];
            card.MinHeight = layout.CardMinHeight;
            Grid.SetRow(card, index / layout.Columns);
            Grid.SetColumn(card, index % layout.Columns);
        }
    }

    internal static (
        double MaxWidth,
        double PagePadding,
        double SectionGap,
        double CardGap,
        double CardMinHeight,
        double PrimaryHeight,
        double HeadingSize,
        int Columns) ResolveLayout(bool compact, bool medium) =>
        compact
            ? (640, 16, 20, 12, 104, 56, 21, 1)
            : medium
                ? (900, 24, 26, 16, 124, 60, 24, 2)
                : (1180, 32, 32, 20, 132, 64, 28, 3);

    internal static bool IsSupportedModuleRoute(string? route) =>
        route is "aeda-code" or
            "aeda-memory" or
            "aeda-research" or
            "aeda-task-center" or
            "aeda-assist";

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as AvaloniaChatViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateStatusText();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AvaloniaChatViewModel.Status) or
            nameof(AvaloniaChatViewModel.StatusMessage))
        {
            UpdateStatusText();
        }
    }

    private void UpdateStatusText()
    {
        AssistantStatusText.Text = _viewModel is null
            ? string.Empty
            : ChatPresentation.DescribeStatus(
                _viewModel.Status,
                _viewModel.StatusMessage);
    }

    private void OnOpenChatClick(object? sender, RoutedEventArgs e) =>
        OpenChatRequested?.Invoke(this, EventArgs.Empty);

    private void OnOpenModuleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string route } && IsSupportedModuleRoute(route))
        {
            OpenModuleRequested?.Invoke(this, new DashboardModuleRequestedEventArgs(route));
        }
    }
}
