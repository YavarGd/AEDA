using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PersonalAI.Desktop.Presentation.ViewModels;
using Windows.UI.ViewManagement;

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
    private readonly bool _animationsEnabled = ReadAnimationsEnabled();
    private bool _routeCompact;
    private bool _routeMedium;
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
            nameof(AssistPillViewModel.State) or
            nameof(AssistPillViewModel.HasResponse))
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
        var idle = DataContext is AssistPillViewModel { IsIdle: true };
        var enabled = _viewModel?.IsEnabled == true;
        var state = _viewModel?.State;

        CompactPill.IsEnabled = _viewModel?.IsEnabled ?? false;
        CompactPill.IsVisible = compact && idle;
        ExpandedBackground.IsVisible = compact && !idle;
        SetStateClasses(CompactPill, state);
        SetStateClasses(ExpandedBackground, state);
        SetStateClasses(StateSurface, state);
        SetStateClasses(ExpandedIdentity, state);
        ExpandedIdentity.Classes.Set("motion", _animationsEnabled);
        AssistListeningMark.IsVisible = state == AssistPillState.DetectingContext;
        AssistThinkingMark.IsVisible = state == AssistPillState.StreamingResponse;
        AssistActionReadyMark.IsVisible = state == AssistPillState.Completed;
        AssistErrorMark.IsVisible = state == AssistPillState.Failed;
        AssistCancelledMark.IsVisible = state == AssistPillState.Cancelled;
        FullSurface.IsVisible = !(compact && idle);
        ModuleHeader.IsVisible = !compact;
        StatusRow.IsVisible = enabled;
        DisabledSurface.IsVisible = !enabled;
        ResponseSurface.RowDefinitions[0].Height = _viewModel?.HasResponse == true
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        ApplyLayout(compact);
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

        if (DataContext is AssistPillViewModel { IsIdle: true })
        {
            AskButton.Focus();
        }
    }

    public void ApplyResponsiveMode(bool compact, bool medium)
    {
        if (_hostMode == AssistViewHostMode.CompactWindow)
        {
            return;
        }

        _routeCompact = compact;
        _routeMedium = medium;
        ApplyLayout(compactWindow: false);
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

    private void ApplyLayout(bool compactWindow)
    {
        if (compactWindow)
        {
            FullSurface.Margin = new Thickness(6);
            FullSurface.RowSpacing = 0;
            StateSurface.Padding = new Thickness(8);
            StateSurface.MaxWidth = double.PositiveInfinity;
            StateSurface.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            StateSurface.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch;
            StateLayout.RowSpacing = 6;
            StatusRow.ColumnSpacing = 10;
            ExpandedIdentity.Width = ExpandedIdentity.Height = 32;
            ExpandedIdentityViewbox.Width = ExpandedIdentityViewbox.Height = 32;
            PromptSurface.Spacing = 8;
            SendButton.Margin = new Thickness(0, 0, 8, 0);
            SelectScreenTextButton.Margin = new Thickness(0);
            return;
        }

        var pagePadding = _routeCompact ? 16 : _routeMedium ? 24 : 32;
        FullSurface.Margin = new Thickness(pagePadding);
        FullSurface.RowSpacing = _routeCompact ? 16 : _routeMedium ? 20 : 24;
        ModuleTitle.FontSize = _routeCompact ? 22 : _routeMedium ? 24 : 28;
        StateSurface.Padding = new Thickness(_routeCompact ? 16 : _routeMedium ? 20 : 24);
        StateSurface.MaxWidth = _routeCompact || _routeMedium
            ? double.PositiveInfinity
            : 860;
        StateSurface.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
        StateSurface.VerticalAlignment = _viewModel?.HasResponse == true
            ? global::Avalonia.Layout.VerticalAlignment.Stretch
            : global::Avalonia.Layout.VerticalAlignment.Top;
        StateLayout.RowSpacing = _routeCompact ? 14 : _routeMedium ? 16 : 18;
        StatusRow.ColumnSpacing = 14;
        ExpandedIdentity.Width = ExpandedIdentity.Height = 44;
        ExpandedIdentityViewbox.Width = ExpandedIdentityViewbox.Height = 44;
        PromptSurface.Spacing = 10;
        SendButton.Margin = new Thickness(0, 0, 8, 8);
        SelectScreenTextButton.Margin = new Thickness(0, 0, 8, 8);
    }

    private static void SetStateClasses(StyledElement element, AssistPillState? state)
    {
        element.Classes.Set("listening", state == AssistPillState.DetectingContext);
        element.Classes.Set("thinking", state == AssistPillState.StreamingResponse);
        element.Classes.Set("actionReady", state == AssistPillState.Completed);
        element.Classes.Set("cancelled", state == AssistPillState.Cancelled);
        element.Classes.Set("error", state == AssistPillState.Failed);
    }

    private static bool ReadAnimationsEnabled()
    {
        try
        {
            return new UISettings().AnimationsEnabled;
        }
        catch (Exception exception) when (
            exception is COMException or
            PlatformNotSupportedException or
            TypeInitializationException)
        {
            return false;
        }
    }
}
