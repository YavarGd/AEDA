using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PersonalAI.Core.Tasks;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Desktop.Avalonia.Views.Tasks;

public partial class TaskCenterView : UserControl
{
    private bool _loaded;
    private bool _compact;
    private bool _medium;
    private CompactPane _compactPane = CompactPane.Overview;
    private CompactPane _originQueue = CompactPane.Overview;
    private WeakReference<Button>? _lastOverviewButton;
    private WeakReference<Button>? _lastTaskButton;

    public TaskCenterView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public void FocusPrimaryAction() => RefreshButton.Focus();

    public void ApplyResponsiveMode(bool compact, bool medium)
    {
        var enteringCompact = compact && !_compact;
        _compact = compact;
        _medium = medium;
        if (enteringCompact)
        {
            _compactPane = CompactPane.Overview;
        }

        Classes.Set("compact", compact);
        Classes.Set("medium", medium);

        var layout = ResolveLayout(compact, medium);
        PageLayout.Margin = new Thickness(layout.PagePadding);
        PageLayout.RowSpacing = layout.MajorGap;
        HeaderSurface.Padding = new Thickness(layout.HeaderPadding);
        PageHeading.FontSize = layout.HeadingSize;
        ContentStack.Spacing = layout.MajorGap;
        WorkspaceLayout.ColumnSpacing = layout.MajorGap;
        WorkspaceLayout.RowSpacing = layout.MajorGap;
        SelectedDetailSurface.MinWidth = layout.DetailMinWidth;

        ArrangeWorkspace(layout.RailWidth);
        UpdatePresentation();
    }

    internal static (
        double PagePadding,
        double HeaderPadding,
        double RailWidth,
        double DetailMinWidth,
        double MajorGap,
        double HeadingSize) ResolveLayout(bool compact, bool medium) =>
        compact
            ? (16, 16, 0, 0, 16, 22)
            : medium
                ? (24, 20, 0, 0, 20, 24)
                : (32, 24, 420, 500, 24, 28);

    private async void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        if (_loaded || DataContext is not AedaTaskCenterViewModel viewModel)
        {
            return;
        }

        _loaded = true;
        await viewModel.RefreshAsync();
    }

    private async void OnTaskClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AedaTaskSummary task } button ||
            DataContext is not AedaTaskCenterViewModel viewModel)
        {
            return;
        }

        await viewModel.SelectTaskAsync(task);
        if (!_compact || viewModel.SelectedTask?.Id != task.Id)
        {
            return;
        }

        _originQueue = Enum.TryParse<CompactPane>(button.Tag as string, out var origin) &&
            origin is CompactPane.Active or CompactPane.Recent or CompactPane.Failed
                ? origin
                : CompactPane.Overview;
        _lastTaskButton = new WeakReference<Button>(button);
        _compactPane = CompactPane.SelectedTask;
        UpdatePresentation();
        FocusAfterLayout(SelectedTaskHeading);
    }

    private void OnOpenCompactPaneClick(object? sender, RoutedEventArgs e)
    {
        if (!_compact || sender is not Button button ||
            !Enum.TryParse<CompactPane>(button.Tag as string, out var pane))
        {
            return;
        }

        _lastOverviewButton = new WeakReference<Button>(button);
        _compactPane = pane;
        UpdatePresentation();
        FocusAfterLayout(RegionFor(pane));
    }

    private void OnCompactBackClick(object? sender, RoutedEventArgs e)
    {
        if (!_compact)
        {
            return;
        }

        if (_compactPane == CompactPane.SelectedTask)
        {
            _compactPane = _originQueue;
            UpdatePresentation();
            FocusAfterLayout(
                TryGet(_lastTaskButton),
                RegionFor(_originQueue),
                CompactOverview);
            return;
        }

        _compactPane = CompactPane.Overview;
        UpdatePresentation();
        FocusAfterLayout(TryGet(_lastOverviewButton), CompactOverview);
    }

    private void ArrangeWorkspace(double railWidth)
    {
        if (_compact)
        {
            WorkspaceLayout.ColumnDefinitions[0].Width = GridLength.Star;
            WorkspaceLayout.ColumnDefinitions[1].Width = Zero;
            WorkspaceLayout.RowDefinitions[0].Height = GridLength.Star;
            WorkspaceLayout.RowDefinitions[1].Height = Zero;
            Position(QueueRail, 0, 0);
            Position(SelectedDetailSurface, 0, 0);
            return;
        }

        if (_medium)
        {
            WorkspaceLayout.ColumnDefinitions[0].Width = GridLength.Star;
            WorkspaceLayout.ColumnDefinitions[1].Width = Zero;
            WorkspaceLayout.RowDefinitions[0].Height = GridLength.Auto;
            WorkspaceLayout.RowDefinitions[1].Height = GridLength.Auto;
            Position(QueueRail, 0, 0);
            Position(SelectedDetailSurface, 1, 0);
            return;
        }

        WorkspaceLayout.ColumnDefinitions[0].Width = new GridLength(railWidth);
        WorkspaceLayout.ColumnDefinitions[1].Width = GridLength.Star;
        WorkspaceLayout.RowDefinitions[0].Height = GridLength.Star;
        WorkspaceLayout.RowDefinitions[1].Height = Zero;
        Position(QueueRail, 0, 0);
        Position(SelectedDetailSurface, 0, 1);
    }

    private void UpdatePresentation()
    {
        var overview = _compact && _compactPane == CompactPane.Overview;
        var approvals = _compact && _compactPane == CompactPane.Approvals;
        var active = _compact && _compactPane == CompactPane.Active;
        var recent = _compact && _compactPane == CompactPane.Recent;
        var failed = _compact && _compactPane == CompactPane.Failed;
        var selected = _compact && _compactPane == CompactPane.SelectedTask;

        HeaderSurface.IsVisible = !_compact || overview;
        CompactOverview.IsVisible = overview;
        CompactBackButton.IsVisible = _compact && !overview;
        CompactBackButton.Content = selected
            ? $"Back to {QueueLabel(_originQueue)}"
            : "Back to Task Center overview";
        AutomationProperties.SetName(CompactBackButton, CompactBackButton.Content?.ToString());

        QueueRail.IsVisible = !_compact || approvals || active || recent || failed;
        ApprovalSurface.IsVisible = !_compact || approvals;
        ActiveSurface.IsVisible = !_compact || active;
        RecentSurface.IsVisible = !_compact || recent;
        FailedSurface.IsVisible = !_compact || failed;
        SelectedDetailSurface.IsVisible = !_compact || selected;
    }

    private Control RegionFor(CompactPane pane) => pane switch
    {
        CompactPane.Approvals => ApprovalSurface,
        CompactPane.Active => ActiveSurface,
        CompactPane.Recent => RecentSurface,
        CompactPane.Failed => FailedSurface,
        CompactPane.SelectedTask => SelectedTaskHeading,
        _ => CompactOverview
    };

    private static string QueueLabel(CompactPane pane) => pane switch
    {
        CompactPane.Active => "Active tasks",
        CompactPane.Recent => "Recent tasks",
        CompactPane.Failed => "Failed or cancelled",
        _ => "Task Center overview"
    };

    private void FocusAfterLayout(params Control?[] targets) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (VisualRoot is null)
                {
                    return;
                }

                foreach (var target in targets)
                {
                    if (target?.Focus() == true)
                    {
                        return;
                    }
                }
            },
            DispatcherPriority.Input);

    private static Button? TryGet(WeakReference<Button>? reference) =>
        reference is not null && reference.TryGetTarget(out var button) ? button : null;

    private static void Position(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
    }

    private static GridLength Zero => new(0);

    private enum CompactPane
    {
        Overview,
        Approvals,
        Active,
        Recent,
        Failed,
        SelectedTask
    }
}
