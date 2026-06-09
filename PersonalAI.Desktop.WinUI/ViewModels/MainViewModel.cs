using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using PersonalAI.Core.Chat;
using PersonalAI.Desktop.WinUI.Models;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const string NewChatTitle = "New chat";

    private readonly ConversationSessionService _conversationSession;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly GenerationNavigationGuard _navigationGuard = new();
    private readonly List<Conversation> _allConversations = [];
    private CancellationTokenSource? _generationCancellation;
    private Conversation? _activeConversation;
    private Task? _activeGenerationTask;
    private bool _hasRequestedGenerationCancellation;
    private bool _isSending;

    public MainViewModel(ConversationSessionService conversationSession)
    {
        _conversationSession = conversationSession;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public ObservableCollection<ConversationListItem> Conversations { get; } = [];

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

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

    public bool IsGenerating => Status is ChatStatus.Connecting or ChatStatus.Generating;

    public bool IsTimelineEmpty => Messages.Count == 0;

    public bool HasNoConversations => Conversations.Count == 0;

    public bool CanSend => !IsGenerating && !_isSending && !string.IsNullOrWhiteSpace(Prompt);

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

    partial void OnConversationSearchChanged(string value)
    {
        ApplyConversationFilter();
    }

    partial void OnStatusChanged(ChatStatus value)
    {
        OnPropertyChanged(nameof(IsGenerating));
        OnPropertyChanged(nameof(CanSend));
        SendMessageCommand.NotifyCanExecuteChanged();
        CancelGenerationCommand.NotifyCanExecuteChanged();
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
        SendMessageCommand.NotifyCanExecuteChanged();

        _generationCancellation = new CancellationTokenSource();
        _hasRequestedGenerationCancellation = false;
        Status = ChatStatus.Connecting;
        StatusMessage = "Connecting";
        Prompt = string.Empty;

        var generationTask = ExecuteGenerationAsync(
            prompt,
            model,
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
            SendMessageCommand.NotifyCanExecuteChanged();
            CancelGenerationCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ExecuteGenerationAsync(
        string prompt,
        string model,
        CancellationToken cancellationToken)
    {
        ChatMessageViewModel? assistantMessage = null;
        var assistantResponse = new StringBuilder();

        try
        {
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

            var history = Messages
                .Where(message => message != assistantMessage)
                .Select(message => new ChatMessage(message.Role, message.Content))
                .ToArray();

            await foreach (var chunk in _conversationSession.StreamAsync(
                               model,
                               history,
                               cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    assistantResponse.Append(chunk.Content);
                    AppendAssistantText(assistantMessage, chunk.Content, conversation.Id);
                }
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
        StatusMessage = "Application context migration is not part of this phase.";
    }

    [RelayCommand]
    public void AttachClipboardText()
    {
        StatusMessage = "Clipboard context migration is not part of this phase.";
    }

    [RelayCommand]
    public void CaptureScreenshot()
    {
        StatusMessage = "Screenshot context migration is not part of this phase.";
    }

    [RelayCommand]
    public void RemoveAttachedContext()
    {
        StatusMessage = "No attached context in this phase.";
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
