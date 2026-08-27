using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using PersonalAI.Core.Memory;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Desktop.Avalonia.Views.Memory;

public partial class MemoryView : UserControl
{
    private bool _loaded;
    private bool _compact;
    private bool _medium;
    private MemorySource _source = MemorySource.Recent;
    private CompactPane _compactPane = CompactPane.Overview;
    private Button? _lastOpenButton;
    private Button? _lastOverviewButton;

    public MemoryView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public void FocusPrimaryAction()
    {
        if (_compact)
        {
            _source = MemorySource.Recent;
            _compactPane = CompactPane.MemoryList;
            UpdatePresentation();
        }

        MemorySearchTextBox.Focus();
    }

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
        PageHeading.FontSize = layout.HeadingSize;
        TrustSummary.Padding = new Thickness(layout.TrustPadding);
        WorkspaceLayout.ColumnSpacing = layout.MajorGap;
        WorkspaceLayout.RowSpacing = layout.MajorGap;
        ToolsPanel.Spacing = layout.MajorGap;

        ArrangeWorkspace(layout);
        UpdatePresentation();
    }

    internal static (
        double PagePadding,
        double TrustPadding,
        double SourceRailWidth,
        double RecordListWidth,
        double ToolWidth,
        double MajorGap,
        double HeadingSize) ResolveLayout(bool compact, bool medium) =>
        compact
            ? (16, 16, 0, 0, 0, 16, 22)
            : medium
                ? (24, 20, 0, 320, 0, 20, 24)
                : (32, 24, 200, 380, 320, 24, 28);

    private async void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        if (_compact)
        {
            _compactPane = CompactPane.Overview;
            UpdatePresentation();
        }

        if (_loaded || DataContext is not AedaMemoryModuleViewModel viewModel)
        {
            return;
        }

        _loaded = true;
        await viewModel.InitializeAsync();
    }

    private void OnRecentSourceClick(object? sender, RoutedEventArgs e) =>
        SelectSource(MemorySource.Recent, sender as Button);

    private void OnTaskSourceClick(object? sender, RoutedEventArgs e) =>
        SelectSource(MemorySource.TaskOutcomes, sender as Button);

    private void OnIndexedSourceClick(object? sender, RoutedEventArgs e) =>
        SelectSource(MemorySource.IndexedKnowledge, sender as Button);

    private void OnOpenAddMemoryClick(object? sender, RoutedEventArgs e)
    {
        if (!_compact)
        {
            return;
        }

        _lastOverviewButton = sender as Button;
        _compactPane = CompactPane.AddMemory;
        UpdatePresentation();
    }

    private void OnOpenRetrievalClick(object? sender, RoutedEventArgs e)
    {
        if (!_compact)
        {
            return;
        }

        _lastOverviewButton = sender as Button;
        _compactPane = CompactPane.Retrieval;
        UpdatePresentation();
    }

    private async void OnOpenMemoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AedaMemoryRecordSummary summary } button ||
            DataContext is not AedaMemoryModuleViewModel viewModel)
        {
            return;
        }

        await viewModel.OpenMemoryDetailAsync(summary);
        if (viewModel.SelectedMemory?.Id != summary.Id)
        {
            return;
        }

        _lastOpenButton = button;
        if (_compact)
        {
            _compactPane = CompactPane.SelectedDetail;
            UpdatePresentation();
            Dispatcher.UIThread.Post(
                () => SelectedMemoryDetailHeading.Focus(),
                DispatcherPriority.Input);
        }
    }

    private async void OnArchiveMemoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AedaMemoryRecordSummary summary } button &&
            DataContext is AedaMemoryModuleViewModel viewModel)
        {
            await viewModel.ArchiveMemoryAsync(summary);
            RestoreRecordFocus(button);
        }
    }

    private async void OnDeleteMemoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AedaMemoryRecordSummary summary } button &&
            DataContext is AedaMemoryModuleViewModel viewModel)
        {
            await viewModel.DeleteMemoryAsync(summary);
            RestoreRecordFocus(button);
        }
    }

    private void OnCompactBackClick(object? sender, RoutedEventArgs e)
    {
        if (!_compact)
        {
            return;
        }

        if (_compactPane == CompactPane.SelectedDetail)
        {
            _compactPane = CompactPane.MemoryList;
            UpdatePresentation();
            if (_lastOpenButton?.Focus() != true && !RecordWorkspace.Focus())
            {
                MemorySearchTextBox.Focus();
            }

            return;
        }

        _compactPane = CompactPane.Overview;
        UpdatePresentation();
        if (_lastOverviewButton?.Focus() != true)
        {
            CompactOverview.Focus();
        }
    }

    private void SelectSource(MemorySource source, Button? sourceButton)
    {
        _source = source;
        if (_compact)
        {
            _lastOverviewButton = sourceButton;
            _compactPane = source == MemorySource.IndexedKnowledge
                ? CompactPane.IndexedKnowledge
                : CompactPane.MemoryList;
        }

        UpdatePresentation();
    }

    private void ArrangeWorkspace((
        double PagePadding,
        double TrustPadding,
        double SourceRailWidth,
        double RecordListWidth,
        double ToolWidth,
        double MajorGap,
        double HeadingSize) layout)
    {
        if (_compact)
        {
            SetColumnWidths(GridLength.Star, Zero, Zero, Zero);
            Position(SourceNavigation, 0, 0, 1);
            Position(CompactOverview, 0, 0, 1);
            Position(RecordWorkspace, 0, 0, 1);
            Position(SelectedDetailSurface, 0, 0, 1);
            Position(IndexedKnowledgeSurface, 0, 0, 1);
            Position(ToolsPanel, 0, 0, 1);
            return;
        }

        if (_medium)
        {
            SetColumnWidths(
                new GridLength(layout.RecordListWidth),
                GridLength.Star,
                Zero,
                Zero);
            Position(SourceNavigation, 0, 0, 2);
            Position(RecordWorkspace, 1, 0, 1);
            Position(SelectedDetailSurface, 1, 1, 1);
            Position(IndexedKnowledgeSurface, 1, 0, 2);
            Position(ToolsPanel, 2, 0, 2);
            SourceNavigation.Orientation = Orientation.Horizontal;
            return;
        }

        SetColumnWidths(
            new GridLength(layout.SourceRailWidth),
            new GridLength(layout.RecordListWidth),
            GridLength.Star,
            new GridLength(layout.ToolWidth));
        Position(SourceNavigation, 0, 0, 1);
        Position(RecordWorkspace, 0, 1, 1);
        Position(SelectedDetailSurface, 0, 2, 1);
        Position(IndexedKnowledgeSurface, 0, 1, 2);
        Position(ToolsPanel, 0, 3, 1);
        SourceNavigation.Orientation = Orientation.Vertical;
    }

    private void UpdatePresentation()
    {
        var compactOverview = _compact && _compactPane == CompactPane.Overview;
        var compactList = _compact && _compactPane == CompactPane.MemoryList;
        var compactDetail = _compact && _compactPane == CompactPane.SelectedDetail;
        var compactIndexed = _compact && _compactPane == CompactPane.IndexedKnowledge;
        var compactAdd = _compact && _compactPane == CompactPane.AddMemory;
        var compactRetrieval = _compact && _compactPane == CompactPane.Retrieval;
        var indexed = _source == MemorySource.IndexedKnowledge;

        PageHeader.IsVisible = !_compact || compactOverview;
        TrustSummary.IsVisible = !_compact || compactOverview;
        CompactBackButton.IsVisible = _compact && !compactOverview;
        CompactBackButton.Content = compactDetail
            ? "Back to list"
            : "Back to Memory overview";
        AutomationProperties.SetName(
            CompactBackButton,
            compactDetail ? "Back to memory list" : "Back to Memory overview");

        SourceNavigation.IsVisible = !_compact;
        CompactOverview.IsVisible = compactOverview;
        RecordWorkspace.IsVisible = (!_compact && !indexed) || compactList;
        SelectedDetailSurface.IsVisible = (!_compact && !indexed) || compactDetail;
        IndexedKnowledgeSurface.IsVisible = (!_compact && indexed) || compactIndexed;
        ToolsPanel.IsVisible = !_compact || compactAdd || compactRetrieval;
        AddMemorySurface.IsVisible = !_compact || compactAdd;
        RetrievalSurface.IsVisible = !_compact || compactRetrieval;

        RecentMemoriesSurface.IsVisible = _source == MemorySource.Recent;
        TaskOutcomesSurface.IsVisible = _source == MemorySource.TaskOutcomes;
        RecentSourceButton.Classes.Set("selected", _source == MemorySource.Recent);
        TaskSourceButton.Classes.Set("selected", _source == MemorySource.TaskOutcomes);
        IndexedSourceButton.Classes.Set("selected", indexed);
    }

    private void RestoreRecordFocus(Button actionButton)
    {
        if (actionButton.Focus() != true && !RecordWorkspace.Focus())
        {
            MemorySearchTextBox.Focus();
        }
    }

    private void SetColumnWidths(
        GridLength first,
        GridLength second,
        GridLength third,
        GridLength fourth)
    {
        WorkspaceLayout.ColumnDefinitions[0].Width = first;
        WorkspaceLayout.ColumnDefinitions[1].Width = second;
        WorkspaceLayout.ColumnDefinitions[2].Width = third;
        WorkspaceLayout.ColumnDefinitions[3].Width = fourth;
    }

    private static void Position(Control control, int row, int column, int columnSpan)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetColumnSpan(control, columnSpan);
    }

    private static GridLength Zero => new(0);

    private enum MemorySource
    {
        Recent,
        TaskOutcomes,
        IndexedKnowledge
    }

    private enum CompactPane
    {
        Overview,
        MemoryList,
        SelectedDetail,
        IndexedKnowledge,
        AddMemory,
        Retrieval
    }
}
