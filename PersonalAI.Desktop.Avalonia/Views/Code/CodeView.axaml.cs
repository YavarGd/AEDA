using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Desktop.Avalonia.Views.Code;

public partial class CodeView : UserControl
{
    private bool _loaded;
    private bool _initializing;
    private bool _compact;
    private bool _medium;
    private bool _compactProposalDetailActive;
    private bool _compactTimelineDetailActive;
    private Button? _lastProposalButton;
    private Button? _lastTaskButton;

    public CodeView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public void FocusPrimaryAction()
    {
        WorkflowTabs.SelectedIndex = 0;
        _compactProposalDetailActive = false;
        UpdateCompactPresentation();
        ProposalRequestBox.Focus();
    }

    public void ApplyResponsiveMode(bool compact, bool medium)
    {
        var enteringCompact = compact && !_compact;
        _compact = compact;
        _medium = medium;
        if (enteringCompact)
        {
            _compactProposalDetailActive = false;
            _compactTimelineDetailActive = false;
        }

        Classes.Set("compact", compact);
        Classes.Set("medium", medium);
        var layout = ResolveLayout(compact, medium);
        PageLayout.Margin = new Thickness(layout.PagePadding);
        PageLayout.RowSpacing = layout.StageGap;
        PageHeading.FontSize = layout.HeadingSize;
        WorkspaceSessionSurface.Padding = new Thickness(layout.HeaderPadding);
        ProposalLayout.ColumnSpacing = layout.StageGap;
        TaskLayout.ColumnSpacing = layout.StageGap;
        UpdateCompactPresentation();
    }

    internal static (
        double PagePadding,
        double HeaderPadding,
        double ProposalColumnWidth,
        double TaskColumnWidth,
        double StageGap,
        double HeadingSize) ResolveLayout(bool compact, bool medium) =>
        compact
            ? (16, 16, 0, 0, 16, 22)
            : medium
                ? (24, 20, 320, 280, 20, 24)
                : (32, 24, 400, 340, 24, 28);

    private async void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        if (_loaded || DataContext is not AedaCodeModuleViewModel viewModel)
        {
            return;
        }

        _loaded = true;
        _initializing = true;
        try
        {
            await viewModel.InitializeAsync();
            if (viewModel.SelectedWorkspace is not null)
            {
                await viewModel.SelectWorkspaceAsync(viewModel.SelectedWorkspace);
            }
        }
        finally
        {
            _initializing = false;
        }
    }

    private async void OnWorkspaceSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_initializing ||
            DataContext is not AedaCodeModuleViewModel viewModel)
        {
            return;
        }

        await viewModel.SelectWorkspaceAsync(WorkspacePicker.SelectedItem as AedaCodeWorkspaceItem);
    }

    private async void OnProposalClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AedaCodeProposalItem proposal } &&
            DataContext is AedaCodeModuleViewModel viewModel)
        {
            await viewModel.SelectProposalAsync(proposal);
            if (viewModel.SelectedProposal?.ProposalId != proposal.ProposalId)
            {
                return;
            }

            SelectRow(ref _lastProposalButton, (Button)sender);
            if (_compact)
            {
                _compactProposalDetailActive = true;
                UpdateCompactPresentation();
                ProposalReviewHeading.Focus();
            }
        }
    }

    private async void OnApplyResultClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AedaCodeApplyItem applyResult } &&
            DataContext is AedaCodeModuleViewModel viewModel)
        {
            await viewModel.SelectApplyResultAsync(applyResult);
        }
    }

    private async void OnTaskClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PersonalAI.Core.Tasks.AedaTaskSummary task } &&
            DataContext is AedaCodeModuleViewModel viewModel)
        {
            await viewModel.SelectTaskAsync(task);
            if (viewModel.SelectedTask?.Id != task.Id)
            {
                return;
            }

            SelectRow(ref _lastTaskButton, (Button)sender);
            if (_compact)
            {
                _compactTimelineDetailActive = true;
                UpdateCompactPresentation();
                TimelineHeading.Focus();
            }
        }
    }

    private void OnBackToProposalsClick(object? sender, RoutedEventArgs e)
    {
        if (!_compact)
        {
            return;
        }

        _compactProposalDetailActive = false;
        UpdateCompactPresentation();
        if (_lastProposalButton?.Focus() != true && !ProposalHistoryRegion.Focus())
        {
            ProposalRequestBox.Focus();
        }
    }

    private void OnBackToCodeTasksClick(object? sender, RoutedEventArgs e)
    {
        if (!_compact)
        {
            return;
        }

        _compactTimelineDetailActive = false;
        UpdateCompactPresentation();
        if (_lastTaskButton?.Focus() != true)
        {
            TaskListRegion.Focus();
        }
    }

    private void UpdateCompactPresentation()
    {
        var proposalDetail = _compact && _compactProposalDetailActive;
        ProposalListPane.IsVisible = !_compact || !proposalDetail;
        ProposalDetailPane.IsVisible = !_compact || proposalDetail;
        BackToProposalsButton.IsVisible = proposalDetail;

        var taskDetail = _compact && _compactTimelineDetailActive;
        TaskListPane.IsVisible = !_compact || !taskDetail;
        TaskDetailPane.IsVisible = !_compact || taskDetail;
        BackToCodeTasksButton.IsVisible = taskDetail;

        if (_compact)
        {
            ProposalLayout.ColumnDefinitions[0].Width = proposalDetail
                ? new GridLength(0)
                : GridLength.Star;
            ProposalLayout.ColumnDefinitions[1].Width = proposalDetail
                ? GridLength.Star
                : new GridLength(0);
            TaskLayout.ColumnDefinitions[0].Width = taskDetail
                ? new GridLength(0)
                : GridLength.Star;
            TaskLayout.ColumnDefinitions[1].Width = taskDetail
                ? GridLength.Star
                : new GridLength(0);
            return;
        }

        var layout = ResolveLayout(compact: false, medium: _medium);
        ProposalLayout.ColumnDefinitions[0].Width = new GridLength(layout.ProposalColumnWidth);
        ProposalLayout.ColumnDefinitions[1].Width = GridLength.Star;
        TaskLayout.ColumnDefinitions[0].Width = new GridLength(layout.TaskColumnWidth);
        TaskLayout.ColumnDefinitions[1].Width = GridLength.Star;
    }

    private static void SelectRow(ref Button? selected, Button current)
    {
        selected?.Classes.Remove("selected");
        selected = current;
        selected.Classes.Add("selected");
    }
}
