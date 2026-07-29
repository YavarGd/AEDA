using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Settings;
using PersonalAI.Core.Tasks;
using PersonalAI.Infrastructure.Chat;

namespace PersonalAI.Desktop.Avalonia.ViewModels.Chat;

public sealed class AvaloniaChatViewModel : ObservableObject, IDisposable
{
    public const string SafeFailureText = "Something went wrong. Please try again.";

    private readonly ConversationSessionService _session;
    private readonly IApplicationSettingsService _settings;
    private readonly Action<Action> _dispatch;
    private IReadOnlyList<Conversation> _allConversations = [];
    private CancellationTokenSource? _sendCancellation;
    private Conversation? _activeConversation;
    private string _searchText = string.Empty;
    private string _draft = string.Empty;
    private string? _statusMessage;
    private bool _isGenerating;
    private ChatStatus _status = ChatStatus.Ready;

    public AvaloniaChatViewModel(
        ConversationSessionService session,
        IApplicationSettingsService settings,
        Action<Action> dispatch)
    {
        _session = session;
        _settings = settings;
        _dispatch = dispatch;
        SendCommand = new AsyncRelayCommand(SendAsync, CanSend);
        CancelCommand = new RelayCommand(Cancel, () => IsGenerating);
        NewChatCommand = new RelayCommand(NewChat, () => !IsGenerating);
    }

    public ObservableCollection<Conversation> Conversations { get; } = [];

    public ObservableCollection<ChatMessageItem> Messages { get; } = [];

    public IAsyncRelayCommand SendCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand NewChatCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _dispatch(ApplyConversationFilter);
            }
        }
    }

    public string Draft
    {
        get => _draft;
        set
        {
            if (SetProperty(ref _draft, value))
            {
                SendCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsGenerating
    {
        get => _isGenerating;
        private set
        {
            if (SetProperty(ref _isGenerating, value))
            {
                SendCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                NewChatCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Conversation? ActiveConversation => _activeConversation;

    public ChatStatus Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await RefreshConversationsAsync(cancellationToken);

    public async Task RefreshConversationsAsync(CancellationToken cancellationToken = default)
    {
        var conversations = await _session.LoadConversationsAsync(cancellationToken);
        _dispatch(() =>
        {
            _allConversations = conversations;
            ApplyConversationFilter();
        });
    }

    public async Task OpenConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        if (IsGenerating)
        {
            return;
        }

        var conversation = await _session.LoadConversationAsync(conversationId, cancellationToken);
        if (conversation is null)
        {
            return;
        }

        var messages = await _session.LoadMessagesAsync(conversationId, cancellationToken);
        _dispatch(() =>
        {
            _activeConversation = conversation;
            Messages.Clear();
            foreach (var message in messages)
            {
                Messages.Add(new ChatMessageItem(message.Role, message.Content));
            }
            Status = ToChatStatus(conversation.Status);
            StatusMessage = conversation.Status == ConversationStatus.Error
                ? SafeFailureText
                : null;
            OnPropertyChanged(nameof(ActiveConversation));
        });
    }

    public void NewChat()
    {
        if (IsGenerating)
        {
            return;
        }

        Draft = string.Empty;
        StatusMessage = null;
        _dispatch(() =>
        {
            _activeConversation = null;
            Messages.Clear();
            Status = ChatStatus.Ready;
            StatusMessage = null;
            OnPropertyChanged(nameof(ActiveConversation));
        });
    }

    public async Task SendAsync()
    {
        if (!CanSend())
        {
            return;
        }

        var prompt = Draft.Trim();
        Draft = string.Empty;
        StatusMessage = null;
        IsGenerating = true;
        SetStatus(ChatStatus.Generating);
        var cancellation = new CancellationTokenSource();
        _sendCancellation = cancellation;
        var response = new StringBuilder();
        var assistantPersisted = false;
        ChatMessageItem? assistant = null;
        Conversation? conversation = _activeConversation;
        TaskId? taskId = null;
        var model = GetGeneralModel();

        try
        {
            if (conversation is null)
            {
                conversation = await _session.CreateConversationAsync(prompt, model, CancellationToken.None);
                SetActiveConversation(conversation);
            }

            var stored = await _session.LoadMessagesAsync(conversation.Id, CancellationToken.None);
            await _session.AddMessageAsync(conversation.Id, ChatRole.User, prompt, CancellationToken.None);
            taskId = await _session.StartChatTaskAsync(conversation.Id, prompt, model, CancellationToken.None);
            _dispatch(() => Messages.Add(new ChatMessageItem(ChatRole.User, prompt)));
            assistant = new ChatMessageItem(ChatRole.Assistant, string.Empty);
            _dispatch(() => Messages.Add(assistant));
            var history = stored.Where(message => message.Role != ChatRole.Tool)
                .Select(message => new ChatMessage(message.Role, message.Content))
                .Append(new ChatMessage(ChatRole.User, prompt))
                .ToArray();

            await foreach (var chunk in _session.StreamWithWorkspaceToolsAsync(
                               conversation.Id,
                               taskId.Value,
                               model,
                               history,
                               cancellation.Token))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    response.Append(chunk.Content);
                    var text = response.ToString();
                    _dispatch(() => assistant.Content = text);
                }
            }

            assistantPersisted = await PersistAssistantAsync(
                conversation.Id,
                response.ToString(),
                assistantPersisted);
            await PersistCompletedAsync(
                conversation,
                taskId.Value,
                response.ToString(),
                model);
        }
        catch (OperationCanceledException)
        {
            if (conversation is not null)
            {
                assistantPersisted = await RecoverInterruptedTurnAsync(
                    conversation,
                    taskId,
                    response.ToString(),
                    model,
                    ConversationStatus.Cancelled,
                    assistantPersisted);
            }

            SetStatus(ChatStatus.Cancelled);
        }
        catch
        {
            if (conversation is not null)
            {
                assistantPersisted = await RecoverInterruptedTurnAsync(
                    conversation,
                    taskId,
                    response.ToString(),
                    model,
                    ConversationStatus.Error,
                    assistantPersisted);
            }

            _dispatch(() => StatusMessage = SafeFailureText);
            SetStatus(ChatStatus.Failed);
        }
        finally
        {
            if (ReferenceEquals(_sendCancellation, cancellation))
            {
                _sendCancellation = null;
            }

            cancellation.Dispose();
            _dispatch(() => IsGenerating = false);
            try
            {
                await RefreshConversationsAsync();
            }
            catch
            {
            }
        }
    }

    public void Cancel() => _sendCancellation?.Cancel();

    public void Dispose()
    {
        _sendCancellation?.Cancel();
    }

    private bool CanSend() => !IsGenerating && !string.IsNullOrWhiteSpace(Draft);

    private string GetGeneralModel() => _settings.Current.Models.Assignments
        .FirstOrDefault(assignment => assignment.Category == ModelRoutingCategory.General)?.Model
        ?? ModelRoutingSettings.DefaultModel;

    private async Task PersistCompletedAsync(
        Conversation conversation,
        TaskId taskId,
        string response,
        string model)
    {
        var updated = await _session.UpdateConversationAsync(
            conversation, ConversationStatus.Completed, model, CancellationToken.None);
        SetActiveConversation(updated);
        SetStatus(ChatStatus.Completed);
        await _session.CompleteChatTaskAsync(taskId, response, CancellationToken.None);
    }

    private async Task<bool> RecoverInterruptedTurnAsync(
        Conversation conversation,
        TaskId? taskId,
        string response,
        string model,
        ConversationStatus status,
        bool assistantPersisted)
    {
        try
        {
            assistantPersisted = await PersistAssistantAsync(
                conversation.Id, response, assistantPersisted);
        }
        catch
        {
        }

        try
        {
            var updated = await _session.UpdateConversationAsync(
                conversation, status, model, CancellationToken.None);
            SetActiveConversation(updated);
        }
        catch
        {
        }

        if (taskId is { } id)
        {
            try
            {
                if (status == ConversationStatus.Cancelled)
                {
                    await _session.CancelChatTaskAsync(id, CancellationToken.None);
                }
                else
                {
                    await _session.FailChatTaskAsync(id, "assistant_response_failed", CancellationToken.None);
                }
            }
            catch
            {
            }
        }

        return assistantPersisted;
    }

    private async Task<bool> PersistAssistantAsync(
        Guid conversationId,
        string response,
        bool assistantPersisted)
    {
        if (!assistantPersisted && !string.IsNullOrWhiteSpace(response))
        {
            await _session.AddMessageAsync(
                conversationId, ChatRole.Assistant, response, CancellationToken.None);
            return true;
        }

        return assistantPersisted;
    }

    private void ApplyConversationFilter()
    {
        var filtered = ConversationSearch.FilterByTitle(_allConversations, SearchText);
        Conversations.Clear();
        foreach (var conversation in filtered)
        {
            Conversations.Add(conversation);
        }
    }

    private void SetActiveConversation(Conversation? conversation) => _dispatch(() =>
    {
        _activeConversation = conversation;
        OnPropertyChanged(nameof(ActiveConversation));
    });

    private void SetStatus(ChatStatus status) => _dispatch(() => Status = status);

    private static ChatStatus ToChatStatus(ConversationStatus status) => status switch
    {
        ConversationStatus.Active => ChatStatus.Ready,
        ConversationStatus.Completed => ChatStatus.Completed,
        ConversationStatus.Cancelled => ChatStatus.Cancelled,
        _ => ChatStatus.Failed
    };
}

public sealed class ChatMessageItem : ObservableObject
{
    private string _content;

    public ChatMessageItem(ChatRole role, string content)
    {
        Role = role;
        _content = content;
    }

    public ChatRole Role { get; }

    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }
}
