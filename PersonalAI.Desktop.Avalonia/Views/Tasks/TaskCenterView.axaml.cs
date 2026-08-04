using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PersonalAI.Core.Tasks;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Desktop.Avalonia.Views.Tasks;

public partial class TaskCenterView : UserControl
{
    private bool _loaded;

    public TaskCenterView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public void FocusPrimaryAction() => RefreshButton.Focus();

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
        if (sender is Button { DataContext: AedaTaskSummary task } &&
            DataContext is AedaTaskCenterViewModel viewModel)
        {
            await viewModel.SelectTaskAsync(task);
        }
    }
}
