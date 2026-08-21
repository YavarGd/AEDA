using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Desktop.Avalonia.Views.Assist;

/// <summary>How an <see cref="AssistView"/> is being hosted.</summary>
public enum AssistViewHostMode
{
    /// <summary>The in-app Assist screen: a full-size module page.</summary>
    FullModule,

    /// <summary>
    /// The floating Assist window, which is only 52x52 while idle. The module chrome
    /// (28px root margin, 26pt heading, status line) cannot fit there, so idle renders a
    /// single Pill surface instead.
    /// </summary>
    CompactWindow
}

public partial class AssistView : UserControl
{
    private AssistViewHostMode _hostMode = AssistViewHostMode.FullModule;

    private AssistPillViewModel? _viewModel;

    public AssistView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ApplyHostMode();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as AssistPillViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplyHostMode();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Idle drives whether the compact window shows the Pill or the expanded surface.
        if (e.PropertyName is nameof(AssistPillViewModel.IsIdle) or
            nameof(AssistPillViewModel.IsEnabled) or
            nameof(AssistPillViewModel.State))
        {
            ApplyHostMode();
        }
    }

    /// <summary>
    /// Selected explicitly by the host. Compactness is deliberately NOT inferred from
    /// <c>IsIdle</c>, because the in-app Assist screen is idle too and must stay full size.
    /// </summary>
    public AssistViewHostMode HostMode
    {
        get => _hostMode;
        set
        {
            if (_hostMode == value)
            {
                return;
            }

            _hostMode = value;
            ApplyHostMode();
        }
    }

    private void ApplyHostMode()
    {
        var compact = _hostMode == AssistViewHostMode.CompactWindow;

        // Idle in the compact window shows only the Pill; every other state expands the
        // window, so the surface is shown with compact margins rather than module spacing.
        var idle = DataContext is AssistPillViewModel { IsIdle: true };
        CompactPill.IsEnabled = _viewModel?.IsEnabled ?? false;
        CompactPill.IsVisible = compact && idle;
        ExpandedBackground.IsVisible = !(compact && idle);
        var state = _viewModel?.State;
        CompactPill.Classes.Set("listening", state == AssistPillState.DetectingContext);
        CompactPill.Classes.Set("thinking", state == AssistPillState.StreamingResponse);
        CompactPill.Classes.Set("actionReady", state == AssistPillState.Completed);
        CompactPill.Classes.Set("error", state == AssistPillState.Failed);
        ExpandedBackground.Classes.Set("listening", state == AssistPillState.DetectingContext);
        ExpandedBackground.Classes.Set("thinking", state == AssistPillState.StreamingResponse);
        ExpandedBackground.Classes.Set("actionReady", state == AssistPillState.Completed);
        ExpandedBackground.Classes.Set("error", state == AssistPillState.Failed);
        AssistIdleMark.IsVisible = state is null or AssistPillState.IdlePill;
        AssistListeningMark.IsVisible = state == AssistPillState.DetectingContext;
        AssistThinkingMark.IsVisible = state == AssistPillState.StreamingResponse;
        AssistActionReadyMark.IsVisible = state == AssistPillState.Completed;
        AssistErrorMark.IsVisible = state == AssistPillState.Failed;
        FullSurface.IsVisible = !(compact && idle);
        FullSurface.Margin = compact ? new Thickness(12) : new Thickness(28);
        ModuleHeader.IsVisible = !compact;
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
