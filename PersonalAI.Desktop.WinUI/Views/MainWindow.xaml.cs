using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PersonalAI.Desktop.WinUI.Models;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Desktop.WinUI.ViewModels;

namespace PersonalAI.Desktop.WinUI.Views;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private ScrollViewer? _timelineScrollViewer;
    private bool _isTimelinePinnedToBottom = true;
    private bool _hasNewerTimelineContent;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop
            {
                Kind = MicaKind.Base
            };
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }

        ApplyTitleBarColors(ElementTheme.Default);
        AedaWindowChrome.Apply(
            AppWindow,
            WinRT.Interop.WindowNative.GetWindowHandle(this),
            Root.ActualTheme == ElementTheme.Dark);
        Root.DataContext = _viewModel;
        _viewModel.ConfirmStopGenerationAsync = ShowStopGenerationDialogAsync;
        _viewModel.ConfirmClearAllContextsAsync = ShowClearAllContextsDialogAsync;
        _viewModel.Settings.Workspaces.ConfirmRemoveWorkspaceAsync =
            ShowRemoveWorkspaceDialogAsync;
        _viewModel.Settings.Workspaces.RequestRenameWorkspaceAsync =
            ShowRenameWorkspaceDialogAsync;
        PromptTextBox.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(PromptTextBox_KeyDown),
            handledEventsToo: true);
        _viewModel.Messages.CollectionChanged += Messages_CollectionChanged;
        TimelineList.Loaded += (_, _) =>
        {
            _timelineScrollViewer ??= FindDescendant<ScrollViewer>(TimelineList);
            if (_timelineScrollViewer is not null)
            {
                _timelineScrollViewer.ViewChanged += TimelineScrollViewer_ViewChanged;
            }

            ScrollTimelineToLatest();
        };
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
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        var shiftDown = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var action = ComposerKeyboardInteraction.ForEnter(
            shiftDown,
            _viewModel.SendMessageCommand.CanExecute(null),
            PromptTextBox.Text);

        if (action == ComposerKeyboardAction.Send)
        {
            e.Handled = true;
            _viewModel.SendMessageCommand.Execute(null);
        }
    }

    public void FocusPromptInput()
    {
        PromptTextBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        PromptTextBox.SelectionStart = PromptTextBox.Text.Length;
    }

    public void ApplyTheme(ElementTheme theme)
    {
        Root.RequestedTheme = theme;
        ApplyTitleBarColors(theme);
        AedaWindowChrome.Apply(
            AppWindow,
            WinRT.Interop.WindowNative.GetWindowHandle(this),
            theme == ElementTheme.Dark);
    }

    private void ApplyTitleBarColors(ElementTheme theme)
    {
        var titleBar = AppWindow.TitleBar;
        var resources = Application.Current.Resources;
        var background = (Windows.UI.Color)resources["AedaTitleBarBackgroundColor"];
        var foreground = (Windows.UI.Color)resources["AedaTitleBarForegroundColor"];
        var hoverBackground = (Windows.UI.Color)resources["AedaTitleBarHoverColor"];
        var pressedBackground = (Windows.UI.Color)resources["AedaTitleBarPressedColor"];

        titleBar.BackgroundColor = background;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape)
        {
            return;
        }

        if (ComposerKeyboardInteraction.ForEscape(
                _viewModel.CancelGenerationCommand.CanExecute(null)) ==
            ComposerKeyboardAction.CancelGeneration)
        {
            e.Handled = true;
            _viewModel.CancelGenerationCommand.Execute(null);
        }
    }

    public XamlRoot? ApprovalXamlRoot => Root.XamlRoot;

    private void Messages_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<ChatMessageViewModel>())
            {
                item.PropertyChanged += Message_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<ChatMessageViewModel>())
            {
                item.PropertyChanged -= Message_PropertyChanged;
            }
        }

        OnTimelineContentChanged();
    }

    private void Message_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatMessageViewModel.Content) or
            nameof(ChatMessageViewModel.RenderedContent))
        {
            OnTimelineContentChanged();
        }
    }

    private void OnTimelineContentChanged()
    {
        if (_isTimelinePinnedToBottom)
        {
            ScrollTimelineToLatest();
            return;
        }

        _hasNewerTimelineContent = true;
        UpdateJumpToLatestVisibility();
    }

    private void TimelineScrollViewer_ViewChanged(
        object? sender,
        ScrollViewerViewChangedEventArgs e)
    {
        _isTimelinePinnedToBottom = IsNearTimelineBottom();
        if (_isTimelinePinnedToBottom)
        {
            _hasNewerTimelineContent = false;
        }

        UpdateJumpToLatestVisibility();
    }

    private bool IsNearTimelineBottom()
    {
        _timelineScrollViewer ??= FindDescendant<ScrollViewer>(TimelineList);
        if (_timelineScrollViewer is null)
        {
            return true;
        }

        return _timelineScrollViewer.ScrollableHeight -
            _timelineScrollViewer.VerticalOffset <= 48;
    }

    private void ScrollTimelineToLatest()
    {
        _timelineScrollViewer ??= FindDescendant<ScrollViewer>(TimelineList);
        if (_timelineScrollViewer is null)
        {
            return;
        }

        _timelineScrollViewer.ChangeView(
            null,
            _timelineScrollViewer.ScrollableHeight,
            null,
            disableAnimation: true);
        _isTimelinePinnedToBottom = true;
        _hasNewerTimelineContent = false;
        UpdateJumpToLatestVisibility();
    }

    private void JumpToLatestButton_Click(object sender, RoutedEventArgs e)
    {
        ScrollTimelineToLatest();
    }

    private void UpdateJumpToLatestVisibility()
    {
        JumpToLatestButton.Visibility =
            !_isTimelinePinnedToBottom && _hasNewerTimelineContent
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
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

    private async Task<bool> ShowClearAllContextsDialogAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Clear attached context?",
            Content = "This removes all currently attached context from the composer.",
            PrimaryButtonText = "Clear context",
            CloseButtonText = "Keep context",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private async Task<bool> ShowRemoveWorkspaceDialogAsync(
        WorkspaceItemViewModel workspace)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Remove workspace?",
            Content =
                $"Remove '{workspace.DisplayName}' from PersonalAI? The folder and conversation history will not be deleted.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Keep workspace",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private async Task<string?> ShowRenameWorkspaceDialogAsync(
        WorkspaceItemViewModel workspace)
    {
        var nameBox = new TextBox
        {
            Header = "Display name",
            Text = workspace.DisplayName,
            MaxLength = 80
        };

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Rename workspace",
            Content = nameBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? nameBox.Text
            : null;
    }
}
