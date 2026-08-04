using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Desktop.Avalonia.Views.Code;

public partial class CodeView : UserControl
{
    private bool _loaded;
    private bool _initializing;

    public CodeView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public void FocusPrimaryAction() => ProposalRequestBox.Focus();

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
        }
    }

    private async void OnTaskClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PersonalAI.Core.Tasks.AedaTaskSummary task } &&
            DataContext is AedaCodeModuleViewModel viewModel)
        {
            await viewModel.SelectTaskAsync(task);
        }
    }
}
