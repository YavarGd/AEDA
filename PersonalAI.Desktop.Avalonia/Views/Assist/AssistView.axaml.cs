using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Desktop.Avalonia.Views.Assist;

public partial class AssistView : UserControl
{
    public AssistView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Moves focus to the most relevant actionable control for the current state, used
    /// when the shell routes to Assist.
    /// </summary>
    public void FocusPrimaryAction()
    {
        if (DataContext is AssistPillViewModel { IsFallbackInput: true })
        {
            PromptBox.Focus();
            return;
        }

        AskButton.Focus();
    }

    private async void OnAskClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AssistPillViewModel viewModel)
        {
            await viewModel.OpenPromptAsync();
        }
    }

    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (DataContext is not AssistPillViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        if (viewModel.SubmitCommand.CanExecute(null))
        {
            viewModel.SubmitCommand.Execute(null);
        }
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (DataContext is not AssistPillViewModel viewModel || !viewModel.CanCancel)
        {
            return;
        }

        e.Handled = true;
        if (viewModel.CancelCommand.CanExecute(null))
        {
            viewModel.CancelCommand.Execute(null);
        }
    }
}
