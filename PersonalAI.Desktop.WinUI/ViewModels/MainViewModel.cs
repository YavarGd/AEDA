using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Context;
using PersonalAI.Core.Editor;
using PersonalAI.Desktop.WinUI.Models;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const string NewChatTitle = "New chat";

    private readonly ConversationSessionService _conversationSession;
    private readonly ClipboardContextService _clipboardContextService;
    private readonly ActiveWindowContextService _activeWindowContextService;
    private readonly ScreenshotAttachmentService _screenshotAttachmentService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly GenerationNavigationGuard _navigationGuard = new();
    private readonly AttachedContextCollection _attachedContextCollection = new();
    private readonly List<Conversation> _allConversations = [];
    private CancellationTokenSource? _generationCancellation;
    private Conversation? _activeConversation;
    private Task? _activeGenerationTask;
    private bool _hasRequestedGenerationCancellation;
    private bool _isSending;

    public MainViewModel(
        ConversationSessionService conversationSession,
        ClipboardContextService clipboardContextService,
        ActiveWindowContextService activeWindowContextService,
        ScreenshotAttachmentService screenshotAttachmentService)
    {
        _conversationSession = conversationSession;
        _clipboardContextService = clipboardContextService;
        _activeWindowContextService = activeWindowContextService;
        _screenshotAttachmentService = screenshotAttachmentService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public ObservableCollection<ConversationListItem> Conversations { get; } = [];

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<AttachedContextItem> AttachedContexts { get; } = [];

    public IReadOnlyList<string> AvailableModels { get; } = ["gemma4"];

    public Func<GenerationStopConfirmationRequest, Task<bool>> ConfirmStopGenerationAsync { get; set; } =
        _ => Task.FromResult(false);

    [ObservableProperty]
    private string _selectedModel = "gemma4";

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _conversationSearch = string.Empty;

    [ObservableProperty]
    private string _currentConversationTitle = NewChatTitle;

    [ObservableProperty]
    private ChatStatus _status = ChatStatus.Ready;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private ConversationListItem? _selectedConversation;

    [ObservableProperty]
    private bool _isEditorConnected;

    [ObservableProperty]
    private string _editorConnectionStatus = "VS Code disconnected";

    [ObservableProperty]
    private string _contextStatusMessage = "No context attached";

    public bool IsGenerating => Status is ChatStatus.Connecting or ChatStatus.Generating;

    public bool IsTimelineEmpty => Messages.Count == 0;

    public bool HasNoConversations => Conversations.Count == 0;

    public bool CanSend => !IsGenerating &&
        !_isSending &&
        !string.IsNullOrWhiteSpace(Prompt) &&
        !HasUnsupportedImageContext;

    public bool CanModifyContexts => !IsGenerating && !_isSending;

    public bool HasAttachedContexts => AttachedContexts.Count > 0;

    public int AttachedContextCount => AttachedContexts.Count;

    public bool HasUnsupportedImageContext =>
        AttachedContexts.Any(context => context.Images.Count > 0) &&
        !ChatModelCapabilityService.SupportsImages(SelectedModel);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var conversations = await _conversationSession.LoadConversationsAsync(
            cancellationToken);

        _allConversations.Clear();
        _allConversations.AddRange(conversations);
        ApplyConversationFilter();

        if (_allConversations.Count > 0)
        {
            var firstConversation = CreateListItem(_allConversations[0]);
            await SelectConversationAsync(firstConversation);
            return;
        }

        CreateNewDraftConversation();
    }

    partial void OnPromptChanged(string value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedModelChanged(string value)
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(HasUnsupportedImageContext));
        SendMessageCommand.NotifyCanExecuteChanged();
        UpdateImageCapabilityStatus();
    }

    partial void OnConversationSearchChanged(string value)
    {
        ApplyConversationFilter();
    }

    partial void OnStatusChanged(ChatStatus value)
    {
        OnPropertyChanged(nameof(IsGenerating));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanModifyContexts));
        SendMessageCommand.NotifyCanExecuteChanged();
        CancelGenerationCommand.NotifyCanExecuteChanged();
        AttachClipboardContextCommand.NotifyCanExecuteChanged();
        CaptureApplicationContextCommand.NotifyCanExecuteChanged();
        CaptureScreenshotContextCommand.NotifyCanExecuteChanged();
        RemoveContextCommand.NotifyCanExecuteChanged();
        ClearAllContextsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public async Task NewChatAsync()
    {
        if (IsGenerating)
        {
            var result = await _navigationGuard.ConfirmStopAndProceedAsync(
                isGenerating: true,
                confirmStopAsync: () => ConfirmStopGenerationAsync(
                    new GenerationStopConfirmationRequest(
                        "Stop and create new chat",
                        "Keep generating")),
                stopGenerationAsync: StopActiveGenerationAsync,
                stopping: () => StatusMessage = "Stopping current response...");

            if (result != GenerationNavigationResult.Proceed)
            {
                RestoreSelectedConversation();
                return;
            }
        }

        CreateNewDraftConversation();
    }

    [RelayCommand]
    public async Task SelectConversationAsync(ConversationListItem? conversation)
    {
        if (conversation is null)
        {
            return;
        }

        if (_activeConversation?.Id == conversation.Id)
        {
            RestoreSelectedConversation();
            return;
        }

        if (IsGenerating)
        {
            var requestedConversation = conversation;
            var result = await _navigationGuard.ConfirmStopAndProceedAsync(
                isGenerating: true,
                confirmStopAsync: () => ConfirmStopGenerationAsync(
                    new GenerationStopConfirmationRequest(
                        "Stop and switch",
                        "Keep generating")),
                stopGenerationAsync: StopActiveGenerationAsync,
                stopping: () => StatusMessage = "Stopping current response...");

            if (result != GenerationNavigationResult.Proceed)
            {
                RestoreSelectedConversation();
                return;
            }

            await LoadConversationAsync(requestedConversation);
            return;
        }

        await LoadConversationAsync(conversation);
    }

    private void CreateNewDraftConversation()
    {
        _activeConversation = null;
        SelectedConversation = null;
        Messages.Clear();
        ClearAttachedContextItems();
        Prompt = string.Empty;
        CurrentConversationTitle = NewChatTitle;
        Status = ChatStatus.Ready;
        StatusMessage = "Ready";
        NotifyMessageCollectionChanged();
    }

    private async Task LoadConversationAsync(ConversationListItem conversation)
    {
        var storedConversation = await _conversationSession.LoadConversationAsync(
            conversation.Id);

        if (storedConversation is null)
        {
            Status = ChatStatus.Failed;
            StatusMessage = "Conversation was not found.";
            await ReloadConversationListAsync();
            return;
        }

        var messages = await _conversationSession.LoadMessagesAsync(
            storedConversation.Id);

        _activeConversation = storedConversation;
        SelectedModel = storedConversation.Model;
        CurrentConversationTitle = storedConversation.Title;
        Messages.Clear();

        foreach (var message in messages)
        {
            Messages.Add(new ChatMessageViewModel(message.Role, message.Content));
        }

        SelectedConversation = Conversations.FirstOrDefault(
            item => item.Id == storedConversation.Id);
        Status = ChatStatus.Ready;
        StatusMessage = "Ready";
        NotifyMessageCollectionChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task SendMessageAsync()
    {
        var prompt = Prompt.Trim();
        var model = SelectedModel;

        if (_isSending || string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        _isSending = true;
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanModifyContexts));
        SendMessageCommand.NotifyCanExecuteChanged();

        _generationCancellation = new CancellationTokenSource();
        _hasRequestedGenerationCancellation = false;
        Status = ChatStatus.Connecting;
        StatusMessage = "Connecting";
        Prompt = string.Empty;

        var contextSnapshot = _attachedContextCollection.Snapshot();
        var generationTask = ExecuteGenerationAsync(
            prompt,
            model,
            contextSnapshot,
            _generationCancellation.Token);
        _activeGenerationTask = generationTask;

        try
        {
            await generationTask;
        }
        finally
        {
            if (ReferenceEquals(_activeGenerationTask, generationTask))
            {
                _activeGenerationTask = null;
            }

            _generationCancellation.Dispose();
            _generationCancellation = null;
            _hasRequestedGenerationCancellation = false;
            _isSending = false;
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanModifyContexts));
            SendMessageCommand.NotifyCanExecuteChanged();
            CancelGenerationCommand.NotifyCanExecuteChanged();
            AttachClipboardContextCommand.NotifyCanExecuteChanged();
            CaptureApplicationContextCommand.NotifyCanExecuteChanged();
            CaptureScreenshotContextCommand.NotifyCanExecuteChanged();
            RemoveContextCommand.NotifyCanExecuteChanged();
            ClearAllContextsCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ExecuteGenerationAsync(
        string prompt,
        string model,
        IReadOnlyList<AttachedContextItem> contextSnapshot,
        CancellationToken cancellationToken)
    {
        ChatMessageViewModel? assistantMessage = null;
        var assistantResponse = new StringBuilder();
        var requestAccepted = false;

        try
        {
            var previousHistory = Messages
                .Select(message => new ChatMessage(message.Role, message.Content))
                .ToArray();
            var conversation = await EnsureActiveConversationAsync(
                prompt,
                model,
                CancellationToken.None);

            await _conversationSession.AddMessageAsync(
                conversation.Id,
                ChatRole.User,
                prompt,
                CancellationToken.None);

            AddMessage(new ChatMessageViewModel(ChatRole.User, prompt));
            assistantMessage = new ChatMessageViewModel(ChatRole.Assistant, string.Empty);
            AddMessage(assistantMessage);

            Status = ChatStatus.Generating;
            StatusMessage = "Generating";

            var requestMessages = AttachedContextPromptComposer.Compose(
                previousHistory,
                prompt,
                contextSnapshot);

            await foreach (var chunk in _conversationSession.StreamAsync(
                               model,
                               requestMessages,
                               cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    if (!requestAccepted)
                    {
                        requestAccepted = true;
                        ClearAttachedContextItems();
                    }

                    assistantResponse.Append(chunk.Content);
                    AppendAssistantText(assistantMessage, chunk.Content, conversation.Id);
                }
            }

            if (!requestAccepted)
            {
                ClearAttachedContextItems();
            }

            var completedContent = assistantResponse.ToString();

            if (!string.IsNullOrWhiteSpace(completedContent))
            {
                await _conversationSession.AddMessageAsync(
                    conversation.Id,
                    ChatRole.Assistant,
                    completedContent,
                    CancellationToken.None);
            }

            _activeConversation = await _conversationSession.UpdateConversationAsync(
                conversation,
                ConversationStatus.Completed,
                model,
                CancellationToken.None);

            await ReloadConversationListAsync(_activeConversation.Id);
            Status = ChatStatus.Completed;
            StatusMessage = "Completed";
        }
        catch (OperationCanceledException)
        {
            await PersistInterruptedGenerationAsync(
                assistantResponse.ToString(),
                ConversationStatus.Cancelled,
                model);
            Status = ChatStatus.Cancelled;
            StatusMessage = "Cancelled";
        }
        catch (Exception exception)
        {
            await PersistInterruptedGenerationAsync(
                assistantResponse.ToString(),
                ConversationStatus.Error,
                model);
            Status = ChatStatus.Failed;
            StatusMessage = $"Failed: {exception.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(IsGenerating))]
    public void CancelGeneration()
    {
        RequestGenerationCancellation();
    }

    private async Task StopActiveGenerationAsync()
    {
        RequestGenerationCancellation();

        if (_activeGenerationTask is not null)
        {
            await _activeGenerationTask;
        }
    }

    private void RequestGenerationCancellation()
    {
        if (_generationCancellation is null ||
            _hasRequestedGenerationCancellation)
        {
            return;
        }

        _hasRequestedGenerationCancellation = true;
        _generationCancellation.Cancel();
    }

    private void RestoreSelectedConversation()
    {
        SelectedConversation = _activeConversation is null
            ? null
            : Conversations.FirstOrDefault(
                item => item.Id == _activeConversation.Id);
    }

    [RelayCommand]
    public void AttachApplicationContext()
    {
        StatusMessage = "Use Capture app to attach application context.";
    }

    [RelayCommand(CanExecute = nameof(CanModifyContexts))]
    public async Task AttachClipboardContextAsync()
    {
        var item = await _clipboardContextService.CaptureTextAsync();

        if (item is null)
        {
            SetContextStatus("Clipboard text was unavailable.");
            return;
        }

        AddAttachedContext(item, "Clipboard context attached.");
    }

    [RelayCommand(CanExecute = nameof(CanModifyContexts))]
    public async Task CaptureApplicationContextAsync()
    {
        var item = await _activeWindowContextService.CaptureAsync();

        if (item is null)
        {
            SetContextStatus(
                "No usable external application window was available.");
            return;
        }

        AddAttachedContext(item, "Application context attached.");
    }

    [RelayCommand(CanExecute = nameof(CanModifyContexts))]
    public async Task CaptureScreenshotContextAsync()
    {
        SetContextStatus("Capturing screenshot...");
        var item = await _screenshotAttachmentService.CaptureExternalWindowAsync();

        if (item is null)
        {
            SetContextStatus(
                "No usable external window was available for screenshot capture.");
            return;
        }

        AddAttachedContext(item, "Screenshot context attached.");
        UpdateImageCapabilityStatus();
    }

    [RelayCommand(CanExecute = nameof(CanModifyContexts))]
    public void RemoveContext(AttachedContextItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (_attachedContextCollection.Remove(item.Id))
        {
            AttachedContexts.Remove(item);
            ReleaseContextResources(item);
            NotifyAttachedContextsChanged();
            SetContextStatus("Context removed.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifyContexts))]
    public void ClearAllContexts()
    {
        ClearAttachedContextItems();
        SetContextStatus("All attached context cleared.");
    }

    public void ReceiveEditorContext(EditorContextEnvelope envelope)
    {
        IsEditorConnected = true;
        EditorConnectionStatus = "VS Code context received";

        if (string.IsNullOrWhiteSpace(Prompt) &&
            !string.IsNullOrWhiteSpace(envelope.UserPrompt))
        {
            Prompt = envelope.UserPrompt;
        }

        ReplaceAttachedContext(
            AttachedContextType.VsCodeEditor,
            AttachedContextFactory.FromEditorContext(envelope));
        SetContextStatus("VS Code editor context attached.");
    }

    public void SetEditorConnectionState(bool isConnected, string status)
    {
        IsEditorConnected = isConnected;
        EditorConnectionStatus = status;
    }

    private async Task<Conversation> EnsureActiveConversationAsync(
        string firstPrompt,
        string model,
        CancellationToken cancellationToken)
    {
        if (_activeConversation is not null)
        {
            return _activeConversation;
        }

        var conversation = await _conversationSession.CreateConversationAsync(
            firstPrompt,
            model,
            cancellationToken);

        _activeConversation = conversation;
        CurrentConversationTitle = conversation.Title;
        await ReloadConversationListAsync(conversation.Id);
        return conversation;
    }

    private async Task PersistInterruptedGenerationAsync(
        string assistantContent,
        ConversationStatus status,
        string model)
    {
        if (_activeConversation is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(assistantContent))
        {
            await _conversationSession.AddMessageAsync(
                _activeConversation.Id,
                ChatRole.Assistant,
                assistantContent,
                CancellationToken.None);
        }

        _activeConversation = await _conversationSession.UpdateConversationAsync(
            _activeConversation,
            status,
            model,
            CancellationToken.None);

        await ReloadConversationListAsync(_activeConversation.Id);
    }

    private void AddAttachedContext(AttachedContextItem item, string successMessage)
    {
        if (!_attachedContextCollection.Add(item))
        {
            SetContextStatus("That context is already attached.");
            return;
        }

        AttachedContexts.Add(item);
        NotifyAttachedContextsChanged();
        SetContextStatus(successMessage);
    }

    private void ReplaceAttachedContext(
        AttachedContextType contextType,
        AttachedContextItem replacement)
    {
        var existing = AttachedContexts
            .Where(context => context.Type == contextType)
            .ToArray();

        foreach (var item in existing)
        {
            _attachedContextCollection.Remove(item.Id);
            AttachedContexts.Remove(item);
        }

        _attachedContextCollection.Add(replacement);
        AttachedContexts.Add(replacement);
        NotifyAttachedContextsChanged();
    }

    private void ClearAttachedContextItems()
    {
        _attachedContextCollection.Clear();
        foreach (var item in AttachedContexts)
        {
            ReleaseContextResources(item);
        }

        AttachedContexts.Clear();
        NotifyAttachedContextsChanged();
    }

    private void NotifyAttachedContextsChanged()
    {
        OnPropertyChanged(nameof(HasAttachedContexts));
        OnPropertyChanged(nameof(AttachedContextCount));
        OnPropertyChanged(nameof(HasUnsupportedImageContext));
        OnPropertyChanged(nameof(CanSend));
        ClearAllContextsCommand.NotifyCanExecuteChanged();
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    private void SetContextStatus(string message)
    {
        ContextStatusMessage = message;
        StatusMessage = message;
    }

    private void UpdateImageCapabilityStatus()
    {
        if (HasUnsupportedImageContext)
        {
            SetContextStatus(
                $"The selected model '{SelectedModel}' is not configured for image input. Remove the screenshot or switch to a vision model.");
        }
    }

    private void ReleaseContextResources(AttachedContextItem item)
    {
        if (item.Type == AttachedContextType.Screenshot)
        {
            _screenshotAttachmentService.Release(item);
        }
    }

    private async Task ReloadConversationListAsync(Guid? selectedConversationId = null)
    {
        var conversations = await _conversationSession.LoadConversationsAsync();
        _allConversations.Clear();
        _allConversations.AddRange(conversations);
        ApplyConversationFilter(selectedConversationId ?? _activeConversation?.Id);
    }

    private void ApplyConversationFilter(Guid? selectedConversationId = null)
    {
        var selectedId = selectedConversationId ??
            SelectedConversation?.Id ??
            _activeConversation?.Id;
        var filtered = PersonalAI.Core.Chat.ConversationSearch
            .FilterByTitle(_allConversations, ConversationSearch)
            .Select(CreateListItem)
            .ToArray();

        Conversations.Clear();

        foreach (var conversation in filtered)
        {
            Conversations.Add(conversation);
        }

        SelectedConversation = selectedId.HasValue
            ? Conversations.FirstOrDefault(item => item.Id == selectedId.Value)
            : null;

        OnPropertyChanged(nameof(HasNoConversations));
    }

    private static ConversationListItem CreateListItem(Conversation conversation)
    {
        return new ConversationListItem(
            conversation.Id,
            conversation.Title,
            $"{conversation.Model} - {conversation.Status}");
    }

    private void AddMessage(ChatMessageViewModel message)
    {
        Messages.Add(message);
        NotifyMessageCollectionChanged();
    }

    private void AppendAssistantText(
        ChatMessageViewModel assistantMessage,
        string content,
        Guid conversationId)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_activeConversation?.Id != conversationId)
            {
                return;
            }

            assistantMessage.Content += content;
        });
    }

    private void NotifyMessageCollectionChanged()
    {
        OnPropertyChanged(nameof(IsTimelineEmpty));
    }
}
