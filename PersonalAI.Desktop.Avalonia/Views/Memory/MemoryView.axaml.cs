using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PersonalAI.Core.Memory;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Desktop.Avalonia.Views.Memory;

public partial class MemoryView : UserControl
{
    private bool _loaded;

    public MemoryView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public void FocusPrimaryAction() => MemorySearchTextBox.Focus();

    private async void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        if (_loaded || DataContext is not AedaMemoryModuleViewModel viewModel)
        {
            return;
        }

        _loaded = true;
        await viewModel.InitializeAsync();
    }

    private async void OnOpenMemoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AedaMemoryRecordSummary summary } &&
            DataContext is AedaMemoryModuleViewModel viewModel)
        {
            await viewModel.OpenMemoryDetailAsync(summary);
        }
    }

    private async void OnArchiveMemoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AedaMemoryRecordSummary summary } &&
            DataContext is AedaMemoryModuleViewModel viewModel)
        {
            await viewModel.ArchiveMemoryAsync(summary);
        }
    }

    private async void OnDeleteMemoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AedaMemoryRecordSummary summary } &&
            DataContext is AedaMemoryModuleViewModel viewModel)
        {
            await viewModel.DeleteMemoryAsync(summary);
        }
    }
}
