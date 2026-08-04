using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using PersonalAI.Core.Ui;
using PersonalAI.Desktop.Avalonia.Views.Assist;
using PersonalAI.Desktop.Presentation.ViewModels;
using PersonalAI.Infrastructure.Context;

namespace PersonalAI.Desktop.Avalonia.Platform.Windows.Assist;

public sealed class AvaloniaAssistWindow : Window
{
    private const double IdleSize = 52;
    private const double SpotlightWidth = 520;
    private const double SpotlightHeight = 180;
    private readonly AssistPillViewModel _viewModel;
    private readonly ForegroundWindowTracker _foregroundWindowTracker;
    private readonly AvaloniaWindowPlacementService _placement = new();
    private readonly AvaloniaAssistWindowIntegration _integration;
    private readonly AssistView _view;

    public AvaloniaAssistWindow(
        AssistPillViewModel viewModel,
        ForegroundWindowTracker foregroundWindowTracker)
    {
        _viewModel = viewModel;
        _foregroundWindowTracker = foregroundWindowTracker;
        _view = new AssistView { DataContext = viewModel };
        _integration = new AvaloniaAssistWindowIntegration(GetWindowHandle);

        DataContext = viewModel;
        Content = _view;
        Title = "AEDA Assist";
        AutomationProperties.SetName(this, "AEDA Assist");
        CanResize = false;
        ShowActivated = false;
        ShowInTaskbar = false;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        Topmost = true;
        Width = IdleSize;
        Height = IdleSize;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public void ShowIdle()
    {
        _viewModel.ShowIdle();
        ApplyState();
    }

    public async Task ToggleAsync(CancellationToken cancellationToken = default)
    {
        if (_viewModel.IsExpanded)
        {
            _viewModel.Collapse();
            return;
        }

        _integration.CaptureFocusReturnTarget();
        await _viewModel.OpenPromptAsync(cancellationToken);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AssistPillViewModel.State) or
            nameof(AssistPillViewModel.Response))
        {
            ApplyState();
        }
    }

    private void ApplyState()
    {
        if (_viewModel.State == AssistPillState.Hidden)
        {
            Hide();
            _ = _integration.RestoreFocus();
            return;
        }

        SizeForState();
        if (!IsVisible)
        {
            Show();
        }

        Dispatcher.UIThread.Post(PlaceForState, DispatcherPriority.Loaded);
        if (_viewModel.State == AssistPillState.DetectingContext)
        {
            _integration.CaptureFocusReturnTarget();
        }

        if (_viewModel.IsFallbackInput)
        {
            _ = _integration.ActivatePrompt();
            Dispatcher.UIThread.Post(_view.FocusPrimaryAction, DispatcherPriority.Input);
        }
        else
        {
            _ = _integration.ShowIdleWithoutActivation();
            if (_viewModel.IsIdle)
            {
                _ = _integration.RestoreFocus();
            }
        }
    }

    private void SizeForState()
    {
        if (_viewModel.IsIdle)
        {
            Width = IdleSize;
            Height = IdleSize;
            return;
        }

        if (_viewModel.IsFallbackInput)
        {
            Width = SpotlightWidth;
            Height = SpotlightHeight;
            return;
        }

        var screen = TargetScreen();
        var scale = screen?.Scaling ?? Math.Max(0.5, RenderScaling);
        var area = screen is null
            ? new RectBounds(0, 0, 1280, 720)
            : new RectBounds(
                screen.WorkingArea.X,
                screen.WorkingArea.Y,
                screen.WorkingArea.Width,
                screen.WorkingArea.Height);
        var response = AssistResponseSizingPolicy.Calculate(ResponseLength(), area, scale);
        Width = response.Width / scale;
        Height = response.Height / scale;
    }

    private void PlaceForState()
    {
        var screen = TargetScreen() ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var scale = screen.Scaling;
        var width = Width * scale;
        var height = Height * scale;
        var bounds = new RectBounds(
            screen.WorkingArea.X,
            screen.WorkingArea.Y,
            screen.WorkingArea.Width,
            screen.WorkingArea.Height);
        var point = _viewModel.IsIdle
            ? PalettePlacementCalculator.BottomRightInBounds(bounds, width, height, 20 * scale)
            : PalettePlacementCalculator.CenterInBounds(bounds, width, height);
        _placement.Place(this, new WindowPosition(point.X, point.Y));
    }

    private Screen? TargetScreen()
    {
        var target = _foregroundWindowTracker.GetLastValidExternalWindow();
        if (target is null || !GetWindowRect(target.WindowHandle, out var bounds))
        {
            return Screens.Primary;
        }

        return Screens.ScreenFromPoint(new PixelPoint(
            bounds.Left + ((bounds.Right - bounds.Left) / 2),
            bounds.Top + ((bounds.Bottom - bounds.Top) / 2))) ?? Screens.Primary;
    }

    private nint GetWindowHandle() =>
        AvaloniaWindowsProcessIdentity.TryGetWindowIdentity(this, out var identity)
            ? identity.WindowHandle
            : 0;

    private int ResponseLength() => Math.Max(
        _viewModel.Response.Length,
        _viewModel.StatusText.Length);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out WindowRect bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
