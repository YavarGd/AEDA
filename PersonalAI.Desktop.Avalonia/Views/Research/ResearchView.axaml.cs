using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PersonalAI.Core.Research;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Desktop.Avalonia.Views.Research;

public partial class ResearchView : UserControl
{
    private bool _loaded;

    public ResearchView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public void FocusPrimaryAction() => VerificationTextBox.Focus();

    private async void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        if (_loaded || DataContext is not AedaResearchModuleViewModel viewModel)
        {
            return;
        }

        _loaded = true;
        await viewModel.InitializeAsync();
    }

    private void OnReportClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: VerificationReport report } &&
            DataContext is AedaResearchModuleViewModel viewModel)
        {
            viewModel.SelectReportCommand.Execute(report);
        }
    }
}
