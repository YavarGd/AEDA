using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;
using PersonalAI.Desktop.Avalonia.Views.Chat;

namespace PersonalAI.Desktop.Avalonia.Views.Dashboard;

public partial class DashboardView : UserControl
{
    private AvaloniaChatViewModel? _viewModel;

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Raised when the reader asks to go to chat. The shell owns navigation, so the
    /// dashboard only reports the intent.
    /// </summary>
    public event EventHandler? OpenChatRequested;

    public void FocusPrimaryAction() => OpenChatButton.Focus();

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
}
