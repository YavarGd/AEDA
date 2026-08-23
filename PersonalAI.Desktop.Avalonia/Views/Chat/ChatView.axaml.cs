using System.ComponentModel;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PersonalAI.Core.Chat;
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;
using PersonalAI.Infrastructure.Chat;

namespace PersonalAI.Desktop.Avalonia.Views.Chat;

public partial class ChatView : UserControl
{
    private readonly ChatConversationLoadQueue _loadQueue = new();
    private AvaloniaChatViewModel? _viewModel;
    private int _outstandingLoads;
    private bool _compact;
    private bool _compactChatActive;
    private bool _medium;
    private bool _followTail = true;
    private bool _suppressSelectionOpen;

    public ChatView()
    {
        InitializeComponent();
        MessageScroll.ScrollChanged += OnMessageScrollChanged;
        DataContextChanged += OnDataContextChanged;
        ConversationList.Tapped += (_, _) => ShowSelectedActiveConversation();
        ConversationList.KeyDown += OnConversationListKeyDown;

        // A multiline TextBox consumes Enter internally to insert a newline, so a plain
        // bubbling KeyDown handler never sees it and Enter could not send. Handling the
        // tunnelling (preview) phase lets the composer decide first; Shift+Enter is left
        // unhandled so the TextBox still inserts the newline itself.
        Composer.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Moves focus to the composer, used when the shell routes to chat.</summary>
    public void FocusComposer()
    {
        ShowCompactChat();
        Composer.Focus();
    }

    public void ApplyResponsiveMode(bool compact, bool medium)
    {
        var enteringCompact = compact && !_compact;
        _compact = compact;
        _medium = medium;
        if (enteringCompact)
        {
            _compactChatActive = _viewModel?.IsGenerating == true;
        }

        Classes.Set("compact", compact);
        Classes.Set("medium", medium);
        var layout = ResolveLayout(compact, medium);
        ConversationRail.Padding = new Thickness(layout.PagePadding);
        ConversationRail.BorderThickness = compact
            ? new Thickness(0)
            : new Thickness(0, 0, 1, 0);
        ChatHeader.Height = layout.HeaderHeight;
        ChatHeader.Padding = new Thickness(layout.PagePadding, 0);
        MessagesItemsControl.Margin = new Thickness(layout.PagePadding);
        MessagesItemsControl.MaxWidth = layout.MessageMaxWidth;
        ComposerRegion.Padding = new Thickness(layout.PagePadding);
        ComposerSurface.MinHeight = layout.ComposerMinHeight;
        ComposerSurface.Padding = new Thickness(
            10,
            Math.Max(0, (layout.ComposerMinHeight - 46) / 2));
        ComposerSurface.MaxWidth = layout.MessageMaxWidth;
        EmptyConversationTitle.FontSize = layout.EmptyTitleSize;
        UpdateCompactPresentation();
    }

    internal static (
        double RailWidth,
        double HeaderHeight,
        double PagePadding,
        double MessageMaxWidth,
        double ComposerMinHeight,
        double EmptyTitleSize) ResolveLayout(bool compact, bool medium) =>
        compact
            ? (0, 52, 16, double.PositiveInfinity, 44, 20)
            : medium
                ? (240, 56, 22, 580, 48, 22)
                : (300, 60, 28, 720, 52, 26);

    /// <summary>
    /// Applies a shell-level keyboard action. Kept here so the shell does not need to
    /// know about the chat view model.
    /// </summary>
    public void ApplyKeyAction(ChatKeyAction action)
    {
        switch (action)
        {
            case ChatKeyAction.Cancel:
                // Never gated: stopping generation must always work.
                Execute(_viewModel?.CancelCommand);
                break;
            case ChatKeyAction.NewChat:
                StartNewChat();
                break;
            case ChatKeyAction.Send:
                if (IsLoadChainDraining)
                {
                    return;
                }

                Execute(_viewModel?.SendCommand);
                break;
        }
    }

    /// <summary>
    /// True while any queued conversation load is still running. Cancelling the token is
    /// not enough on its own: a load whose reads already completed will still publish to
    /// the UI thread, which would repopulate a cleared or actively generating timeline.
    /// So the actions that replace timeline content wait for the chain to drain.
    /// Re-selecting conversations stays available, because the queue guarantees the
    /// newest selection publishes last.
    /// </summary>
    private bool IsLoadChainDraining => _outstandingLoads > 0;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Nothing may repopulate the timeline after the view leaves the tree.
        _loadQueue.Invalidate();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Queued work belongs to the outgoing view model.
        _loadQueue.Invalidate();

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Messages.CollectionChanged -= OnCollectionChanged;
            _viewModel.Conversations.CollectionChanged -= OnCollectionChanged;
        }

        _viewModel = DataContext as AvaloniaChatViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Messages.CollectionChanged += OnCollectionChanged;
            _viewModel.Conversations.CollectionChanged += OnCollectionChanged;
        }

        UpdateStatusText();
        UpdateConversationListAvailability();
        UpdateEmptyStates();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AvaloniaChatViewModel.Status) or
            nameof(AvaloniaChatViewModel.StatusMessage))
        {
            UpdateStatusText();
        }

        if (e.PropertyName == nameof(AvaloniaChatViewModel.ActiveConversation))
        {
            SyncSelectionToActiveConversation();
        }

        if (e.PropertyName == nameof(AvaloniaChatViewModel.IsGenerating))
        {
            UpdateConversationListAvailability();
        }
    }

    /// <summary>
    /// Opening a conversation is ignored while generating, so the list is disabled to
    /// stop the highlight from drifting away from what is displayed.
    /// </summary>
    private void UpdateConversationListAvailability()
    {
        var available = !(_viewModel?.IsGenerating ?? false);
        ConversationList.IsEnabled = available;
        BackToConversationsButton.IsEnabled = available;
    }

    private void UpdateStatusText()
    {
        if (_viewModel is null)
        {
            StatusText.Text = string.Empty;
            return;
        }

        StatusText.Text = ChatPresentation.DescribeStatus(
            _viewModel.Status,
            _viewModel.StatusMessage);
        var status = _viewModel.Status;
        StatusDot.Classes.Set("success", status is ChatStatus.Ready or ChatStatus.Completed);
        StatusDot.Classes.Set("generating", status == ChatStatus.Generating);
        StatusDot.Classes.Set("failed", status == ChatStatus.Failed);
        StatusDot.Classes.Set(
            "neutral",
            status is ChatStatus.Connecting or ChatStatus.Cancelled);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateEmptyStates();

    private void UpdateEmptyStates()
    {
        var messagesEmpty = _viewModel?.Messages.Count is null or 0;
        EmptyConversationState.IsVisible = messagesEmpty;
        MessagesItemsControl.IsVisible = !messagesEmpty;

        var conversationsEmpty = _viewModel?.Conversations.Count is null or 0;
        ConversationEmptyState.IsVisible = conversationsEmpty;
        ConversationEmptyState.Text = string.IsNullOrWhiteSpace(_viewModel?.SearchText)
            ? "No conversations yet. Start one to see it here."
            : "No conversations match your search.";
    }

    /// <summary>
    /// Keeps the list highlight in step with the view model when a conversation is
    /// created or cleared, without re-opening it.
    /// </summary>
    private void SyncSelectionToActiveConversation()
    {
        if (_viewModel is null)
        {
            return;
        }

        var active = _viewModel.ActiveConversation;
        _suppressSelectionOpen = true;
        try
        {
            if (active is null)
            {
                ConversationList.SelectedItem = null;
                return;
            }

            if (ConversationList.SelectedItem is Conversation selected &&
                selected.Id == active.Id)
            {
                return;
            }

            ConversationList.SelectedItem = _viewModel.Conversations
                .FirstOrDefault(conversation => conversation.Id == active.Id);
        }
        finally
        {
            _suppressSelectionOpen = false;
        }
    }

    private void OnConversationSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionOpen || _viewModel is null)
        {
            return;
        }

        // A1 ignores open requests while generating, which would leave the highlight
        // pointing at a conversation that is not the one on screen.
        if (_viewModel.IsGenerating)
        {
            SyncSelectionToActiveConversation();
            return;
        }

        if (ConversationList.SelectedItem is not Conversation conversation)
        {
            return;
        }

        if (_viewModel.ActiveConversation?.Id == conversation.Id)
        {
            ShowCompactChat();
            return;
        }

        var viewModel = _viewModel;

        // Serialized so the newest selection is always the last to publish; the queue
        // contains every failure, so nothing reaches the dispatcher.
        TrackLoad(_loadQueue.Enqueue(
            async token =>
            {
                var applied = await ChatConversationOpener.OpenAndConfirmAsync(
                    conversation.Id,
                    cancellationToken => viewModel.OpenConversationAsync(
                        conversation.Id,
                        cancellationToken),
                    UiBarrierAsync,
                    () => viewModel.ActiveConversation?.Id,
                    token);

                if (!applied)
                {
                    // The view model declined the open, so put the highlight back.
                    SyncSelectionToActiveConversation();
                    return;
                }

                ShowCompactChat();
                _followTail = true;
                MessageScroll.ScrollToEnd();
            },
            SyncSelectionToActiveConversation));
    }

    private void OnConversationListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && ShowSelectedActiveConversation())
        {
            e.Handled = true;
        }
    }

    private bool ShowSelectedActiveConversation()
    {
        if (!_compact || _compactChatActive ||
            ConversationList.SelectedItem is not Conversation selected ||
            _viewModel?.ActiveConversation?.Id != selected.Id)
        {
            return false;
        }

        ShowCompactChat();
        return true;
    }

    /// <summary>
    /// Counts a queued load as outstanding until its task finishes, so the actions that
    /// replace timeline content stay unavailable until every queued load has run.
    /// </summary>
    private void TrackLoad(Task load)
    {
        _outstandingLoads++;
        UpdateReplacementActionAvailability();
        _ = AwaitLoadAsync(load);
    }

    private async Task AwaitLoadAsync(Task load)
    {
        try
        {
            await load;
        }
        catch
        {
            // The queue already contains failures; this is only drain bookkeeping.
        }

        _outstandingLoads--;
        UpdateReplacementActionAvailability();
    }

    /// <summary>
    /// Disabling the button composes with its command: Avalonia keeps a button disabled
    /// when either this flag or the command says so, so re-enabling here hands control
    /// back to the command.
    /// </summary>
    private void UpdateReplacementActionAvailability()
    {
        var draining = IsLoadChainDraining;
        NewChatButton.IsEnabled = !draining;
        SendButton.IsEnabled = !draining;
    }

    /// <summary>
    /// Completes after work already posted to the UI thread at the same priority, so a
    /// view-model publication is guaranteed to have been applied.
    /// </summary>
    private static async Task UiBarrierAsync() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Default);

    private void OnNewChatClick(object? sender, RoutedEventArgs e)
    {
        if (IsLoadChainDraining)
        {
            return;
        }

        // A pending load must not repopulate the timeline after a new chat is requested.
        _loadQueue.Invalidate();
        _followTail = true;
        ShowCompactChat();
        Composer.Focus();
    }

    /// <summary>
    /// Clicking Send resumes following the tail exactly as Enter does. The command
    /// binding performs the send, and the drain gate keeps the button disabled while a
    /// queued load could still publish.
    /// </summary>
    private void OnSendClick(object? sender, RoutedEventArgs e) => _followTail = true;

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        var action = ChatKeyboardPolicy.ResolveComposerKey(
            e.Key,
            e.KeyModifiers,
            _viewModel?.IsGenerating ?? false);

        switch (action)
        {
            case ChatKeyAction.InsertNewline:
                // Let the TextBox insert the newline itself.
                return;
            case ChatKeyAction.Send:
                e.Handled = true;

                if (IsLoadChainDraining)
                {
                    // A queued load could still publish over the new turn.
                    return;
                }

                _followTail = true;
                Execute(_viewModel?.SendCommand);
                return;
            case ChatKeyAction.Cancel:
                e.Handled = true;
                Execute(_viewModel?.CancelCommand);
                return;
            case ChatKeyAction.NewChat:
                e.Handled = true;
                StartNewChat();
                return;
        }
    }

    private void StartNewChat()
    {
        if (IsLoadChainDraining)
        {
            // A queued load could still publish over the cleared timeline.
            return;
        }

        _loadQueue.Invalidate();
        Execute(_viewModel?.NewChatCommand);
        _followTail = true;
        ShowCompactChat();
        Composer.Focus();
    }

    private void OnBackToConversationsClick(object? sender, RoutedEventArgs e)
    {
        if (!_compact || _viewModel?.IsGenerating == true)
        {
            return;
        }

        _compactChatActive = false;
        UpdateCompactPresentation();
        FocusSelectedConversation();
    }

    private void FocusSelectedConversation()
    {
        if (ConversationList.SelectedItem is { } selected &&
            ConversationList.ContainerFromItem(selected) is Control container)
        {
            container.Focus();
            return;
        }

        if (!ConversationList.Focus())
        {
            SearchBox.Focus();
        }
    }

    private void ShowCompactChat()
    {
        if (!_compact)
        {
            return;
        }

        _compactChatActive = true;
        UpdateCompactPresentation();
    }

    private void UpdateCompactPresentation()
    {
        var showCompactChat = _compact && _compactChatActive;
        ConversationRail.IsVisible = !_compact || !showCompactChat;
        ActiveChatPane.IsVisible = !_compact || showCompactChat;
        BackToConversationsButton.IsVisible = showCompactChat;

        if (!_compact)
        {
            ChatLayout.ColumnDefinitions[0].Width = new GridLength(
                ResolveLayout(compact: false, medium: _medium).RailWidth);
            ChatLayout.ColumnDefinitions[1].Width = GridLength.Star;
            return;
        }

        ChatLayout.ColumnDefinitions[0].Width = showCompactChat
            ? new GridLength(0)
            : GridLength.Star;
        ChatLayout.ColumnDefinitions[1].Width = showCompactChat
            ? GridLength.Star
            : new GridLength(0);
    }

    private static void Execute(System.Windows.Input.ICommand? command)
    {
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }

    /// <summary>
    /// Follows streamed output only while the reader is already at the end. A scroll
    /// that the reader initiates (no extent change) updates that decision.
    /// </summary>
    private void OnMessageScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var contentGrew = e.ExtentDelta.Y != 0;

        _followTail = ChatScrollFollowPolicy.ShouldFollowAfterScrollChange(
            _followTail,
            offsetMoved: e.OffsetDelta.Y != 0,
            contentGrew,
            MessageScroll.Offset.Y,
            MessageScroll.Extent.Height,
            MessageScroll.Viewport.Height);

        if (contentGrew && _followTail)
        {
            MessageScroll.ScrollToEnd();
        }
    }
}
