using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PersonalAI.Desktop.WinUI.Models;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Desktop.WinUI.ViewModels;

namespace PersonalAI.Desktop.WinUI.Views;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        Root.DataContext = _viewModel;
        _viewModel.ConfirmStopGenerationAsync = ShowStopGenerationDialogAsync;
    }

    private void ConversationList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (sender is ListView listView &&
            listView.SelectedItem is ConversationListItem conversation)
        {
            _viewModel.SelectConversationCommand.Execute(conversation);
        }
    }

    private void PromptTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter ||
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Shift).HasFlag(
                Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            return;
        }

        e.Handled = true;

        if (_viewModel.SendMessageCommand.CanExecute(null))
        {
            _viewModel.SendMessageCommand.Execute(null);
        }
    }

    public void FocusPromptInput()
    {
        PromptTextBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        PromptTextBox.SelectionStart = PromptTextBox.Text.Length;
    }

    private async Task<bool> ShowStopGenerationDialogAsync(
        GenerationStopConfirmationRequest request)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Assistant response in progress",
            Content = "PersonalAI is still generating a response for the current conversation.",
            PrimaryButtonText = request.PrimaryButtonText,
            CloseButtonText = request.CloseButtonText,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
