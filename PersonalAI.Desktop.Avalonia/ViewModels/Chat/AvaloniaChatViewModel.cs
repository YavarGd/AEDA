using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Settings;
using PersonalAI.Core.Tasks;
using PersonalAI.Infrastructure.Chat;

namespace PersonalAI.Desktop.Avalonia.ViewModels.Chat;

public sealed class AvaloniaChatViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    public const string SafeFailureText = "Something went wrong. Please try again.";

    private readonly ConversationSessionService _session;
    private readonly IApplicationSettingsService _settings;
    private readonly Action<Action> _dispatch;
    private IReadOnlyList<Conversation> _allConversations = [];
    private CancellationTokenSource? _sendCancellation;
    private Task? _generationTask;
    private int _disposed;
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
        NewChatCommand = new RelayCommand(NewChat, () => !IsGenerating && _disposed == 0);
        RetryCommand = new AsyncRelayCommand<ChatMessageItem?>(RetryAsync, CanRetryMessage);
        RegenerateCommand = new AsyncRelayCommand<ChatMessageItem?>(RegenerateAsync, CanRegenerateMessage);
    }

    public ObservableCollection<Conversation> Conversations { get; } = [];

    public ObservableCollection<ChatMessageItem> Messages { get; } = [];

    public IAsyncRelayCommand SendCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand NewChatCommand { get; }

    public IAsyncRelayCommand<ChatMessageItem?> RetryCommand { get; }

    public IAsyncRelayCommand<ChatMessageItem?> RegenerateCommand { get; }

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
                RetryCommand.NotifyCanExecuteChanged();
                RegenerateCommand.NotifyCanExecuteChanged();
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
            var supersededToolCallIds = FindSupersededToolCallIds(messages);
            for (var index = 0; index < messages.Count; index++)
            {
                var stored = messages[index];
                if (IsSupersededToolCall(stored, supersededToolCallIds))
                {
                    continue;
                }

                var item = new ChatMessageItem(stored.Role, FormatStoredMessage(stored));
                if (stored.Role == ChatRole.Assistant && index == messages.Count - 1)
                {
                    item.Status = ToMessageStatus(conversation.Status);
                }

                Messages.Add(item);
            }

            Status = ToChatStatus(conversation.Status);
            StatusMessage = conversation.Status == ConversationStatus.Error
                ? SafeFailureText
                : null;
            RefreshMessageEligibility();
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
        await ExecuteGenerationAsync(
            prompt,
            persistUserMessage: true,
            priorHistory: null,
            completionStatus: ChatMessageStatus.Completed,
            existingAssistantMessage: null);
    }

    public void Cancel() => _sendCancellation?.Cancel();

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _sendCancellation?.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (_generationTask is null)
        {
            return;
        }

        try
        {
            await _generationTask.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Cancellation is expected. Providers own the quarantine boundary for
            // an uncooperative external operation.
        }
    }

    private bool CanSend() =>
        _disposed == 0 && !IsGenerating && !string.IsNullOrWhiteSpace(Draft);

    private bool CanRetryMessage(ChatMessageItem? message) =>
        _disposed == 0 && !IsGenerating && message?.CanRetry == true;

    private async Task RetryAsync(ChatMessageItem? message)
    {
        if (!CanRetryMessage(message) ||
            !TryGetGenerationPromptFor(message!, out var prompt, out var priorHistory))
        {
            return;
        }

        await ExecuteGenerationAsync(
            prompt,
            persistUserMessage: false,
            priorHistory: priorHistory,
            completionStatus: ChatMessageStatus.Completed,
            existingAssistantMessage: message);
    }

    private bool CanRegenerateMessage(ChatMessageItem? message) =>
        _disposed == 0 && !IsGenerating && message?.CanRegenerate == true;

    private async Task RegenerateAsync(ChatMessageItem? message)
    {
        if (!CanRegenerateMessage(message) ||
            !TryGetGenerationPromptFor(message!, out var prompt, out var priorHistory))
        {
            return;
        }

        await ExecuteGenerationAsync(
            prompt,
            persistUserMessage: false,
            priorHistory: priorHistory,
            completionStatus: ChatMessageStatus.Regenerated,
            existingAssistantMessage: null);
    }

    /// <summary>
    /// Locates the nearest preceding user message for <paramref name="assistantMessage"/> and
    /// returns its content as the prompt to resend, together with the transcript before that
    /// user turn. Ported from the WinUI TryGetGenerationPromptFor semantics: Retry/Regenerate
    /// never re-persist the user turn, they only replay it.
    /// </summary>
    private bool TryGetGenerationPromptFor(
        ChatMessageItem assistantMessage,
        out string prompt,
        out IReadOnlyList<ChatMessage> priorHistory)
    {
        prompt = string.Empty;
        priorHistory = [];
        var assistantIndex = Messages.IndexOf(assistantMessage);
        if (assistantIndex < 0)
        {
            return false;
        }

        var userIndex = -1;
        for (var index = assistantIndex - 1; index >= 0; index--)
        {
            if (Messages[index].Role == ChatRole.User)
            {
                userIndex = index;
                break;
            }
        }

        if (userIndex < 0)
        {
            return false;
        }

        prompt = Messages[userIndex].Content;
        priorHistory = Messages
            .Take(userIndex)
            .Select(message => new ChatMessage(message.Role, message.Content))
            .ToArray();
        return !string.IsNullOrWhiteSpace(prompt);
    }

    /// <summary>
    /// Shared generation routine used by Send, Retry, and Regenerate so the streaming and
    /// persistence logic is not duplicated three times. Send appends a new user turn and a new
    /// assistant message. Retry replays the nearest prior user turn into the existing assistant
    /// message (in place). Regenerate replays the same prompt but appends a new assistant
    /// message, leaving the previous one untouched.
    /// </summary>
    private Task ExecuteGenerationAsync(
        string prompt,
        bool persistUserMessage,
        IReadOnlyList<ChatMessage>? priorHistory,
        ChatMessageStatus completionStatus,
        ChatMessageItem? existingAssistantMessage)
    {
        if (_disposed != 0)
        {
            return Task.CompletedTask;
        }

        return _generationTask = ExecuteGenerationCoreAsync(
            prompt,
            persistUserMessage,
            priorHistory,
            completionStatus,
            existingAssistantMessage);
    }

    private async Task ExecuteGenerationCoreAsync(
        string prompt,
        bool persistUserMessage,
        IReadOnlyList<ChatMessage>? priorHistory,
        ChatMessageStatus completionStatus,
        ChatMessageItem? existingAssistantMessage)
    {
        StatusMessage = null;
        IsGenerating = true;
        SetStatus(ChatStatus.Generating);
        var cancellation = new CancellationTokenSource();
        _sendCancellation = cancellation;
        var response = new StringBuilder();
        var toolActivityMessages = new Dictionary<string, ChatMessageItem>(StringComparer.Ordinal);
        var assistantPersisted = false;
        var assistant = existingAssistantMessage;
        var isNewAssistantMessage = assistant is null;
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

            var conversationId = conversation.Id;

            IReadOnlyList<ChatMessage> history;
            if (priorHistory is not null)
            {
                history = [.. priorHistory, new ChatMessage(ChatRole.User, prompt)];
            }
            else
            {
                var stored = await _session.LoadMessagesAsync(conversationId, CancellationToken.None);
                history = stored.Where(message => message.Role != ChatRole.Tool)
                    .Select(message => new ChatMessage(message.Role, message.Content))
                    .Append(new ChatMessage(ChatRole.User, prompt))
                    .ToArray();
            }

            if (persistUserMessage)
            {
                await _session.AddMessageAsync(conversationId, ChatRole.User, prompt, CancellationToken.None);
            }

            taskId = await _session.StartChatTaskAsync(conversationId, prompt, model, CancellationToken.None);

            if (persistUserMessage)
            {
                _dispatch(() => Messages.Add(new ChatMessageItem(ChatRole.User, prompt)));
            }

            if (isNewAssistantMessage)
            {
                assistant = new ChatMessageItem(ChatRole.Assistant, string.Empty);
                var toAdd = assistant;
                _dispatch(() => Messages.Add(toAdd));
            }
            else
            {
                var existing = assistant!;
                _dispatch(() =>
                {
                    existing.Content = string.Empty;
                    existing.Status = ChatMessageStatus.Streaming;
                });
            }

            await foreach (var chunk in _session.StreamWithWorkspaceToolsAsync(
                               conversationId,
                               taskId.Value,
                               model,
                               history,
                               cancellation.Token))
            {
                if (!string.IsNullOrWhiteSpace(chunk.ActivityMessage))
                {
                    AddOrUpdateToolActivity(chunk, toolActivityMessages);
                    var activity = chunk.ActivityMessage;
                    _dispatch(() => StatusMessage = activity);
                }

                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    response.Append(chunk.Content);
                    var text = response.ToString();
                    var current = assistant!;
                    _dispatch(() => current.Content = text);
                }
            }

            assistantPersisted = await PersistAssistantAsync(
                conversationId,
                response.ToString(),
                assistantPersisted);
            await PersistCompletedAsync(
                conversation,
                taskId.Value,
                response.ToString(),
                model,
                assistant!,
                completionStatus);
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
                    assistantPersisted,
                    assistant);
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
                    assistantPersisted,
                    assistant);
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
            _dispatch(() =>
            {
                IsGenerating = false;
                RefreshMessageEligibility();
            });
            try
            {
                await RefreshConversationsAsync();
            }
            catch
            {
            }
        }
    }

    private string GetGeneralModel() => _settings.Current.Models.Assignments
        .FirstOrDefault(assignment => assignment.Category == ModelRoutingCategory.General)?.Model
        ?? ModelRoutingSettings.DefaultModel;

    private async Task PersistCompletedAsync(
        Conversation conversation,
        TaskId taskId,
        string response,
        string model,
        ChatMessageItem assistant,
        ChatMessageStatus completionStatus)
    {
        var updated = await _session.UpdateConversationAsync(
            conversation, ConversationStatus.Completed, model, CancellationToken.None);
        SetActiveConversation(updated);
        SetStatus(ChatStatus.Completed);

        // Stamp the terminal status and recompute eligibility in the SAME dispatched action.
        // Two separate posts let the refresh observe a stale status on the real dispatcher,
        // which left Regenerate hidden after an otherwise successful turn.
        _dispatch(() =>
        {
            assistant.Status = completionStatus;
            RefreshMessageEligibility();
        });
        await _session.CompleteChatTaskAsync(taskId, response, CancellationToken.None);
    }

    private async Task<bool> RecoverInterruptedTurnAsync(
        Conversation conversation,
        TaskId? taskId,
        string response,
        string model,
        ConversationStatus status,
        bool assistantPersisted,
        ChatMessageItem? assistant)
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

        if (assistant is not null)
        {
            var itemStatus = status == ConversationStatus.Cancelled
                ? ChatMessageStatus.Cancelled
                : ChatMessageStatus.Failed;
            // Same atomicity requirement as the completed path: a separate refresh post
            // could observe the pre-terminal status and leave Retry hidden.
            _dispatch(() =>
            {
                assistant.Status = itemStatus;
                RefreshMessageEligibility();
            });
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

    /// <summary>
    /// Recomputes which message can be retried/regenerated. CanRetry is intrinsic to a message
    /// (assistant + terminal Failed/Cancelled status) and is kept current by ChatMessageItem
    /// itself. CanRegenerate additionally depends on being the latest assistant message, so it
    /// is recomputed here whenever the timeline or a message status changes.
    /// </summary>
    private void RefreshMessageEligibility()
    {
        ChatMessageItem? latestAssistant = null;
        for (var index = Messages.Count - 1; index >= 0; index--)
        {
            if (Messages[index].Role == ChatRole.Assistant)
            {
                latestAssistant = Messages[index];
                break;
            }
        }

        foreach (var message in Messages)
        {
            message.SetCanRegenerate(
                ReferenceEquals(message, latestAssistant) &&
                message.Status == ChatMessageStatus.Completed);
        }

        RetryCommand.NotifyCanExecuteChanged();
        RegenerateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Live tool-activity handling, ported from the WinUI MainViewModel.AddOrUpdateToolActivity.
    /// A chunk with a non-blank ActivityKey that is already tracked for this turn updates that
    /// existing Tool message's content in place (coalescing repeated progress updates for the
    /// same logical activity into one row instead of spamming duplicates). Otherwise a new
    /// ChatRole.Tool message is appended and tracked by key. Assistant text streaming is
    /// entirely separate and untouched by this path.
    /// </summary>
    private void AddOrUpdateToolActivity(
        ChatChunk chunk,
        IDictionary<string, ChatMessageItem> toolActivityMessages)
    {
        if (string.IsNullOrWhiteSpace(chunk.ActivityMessage))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(chunk.ActivityKey) &&
            toolActivityMessages.TryGetValue(chunk.ActivityKey, out var existing))
        {
            var updatedText = chunk.ActivityMessage;
            _dispatch(() => existing.Content = updatedText);
            return;
        }

        var item = new ChatMessageItem(ChatRole.Tool, chunk.ActivityMessage);
        _dispatch(() => Messages.Add(item));

        if (!string.IsNullOrWhiteSpace(chunk.ActivityKey))
        {
            toolActivityMessages[chunk.ActivityKey] = item;
        }
    }

    /// <summary>
    /// Persisted Tool records render as a friendly summary produced by
    /// ToolPresentationMapper.FormatStoredToolActivity rather than the raw JSON stored for the
    /// record, matching the WinUI FormatStoredMessage behaviour.
    /// </summary>
    private static string FormatStoredMessage(StoredChatMessage message) =>
        message.Role != ChatRole.Tool
            ? message.Content
            : ToolPresentationMapper.FormatStoredToolActivity(message.Content);

    /// <summary>
    /// A tool_call record is superseded once a later Tool record carries a matching
    /// toolCallId as its result. Ported from WinUI's FindSupersededToolCallIds /
    /// IsSupersededToolCall so a reopened conversation does not show the "requested" row next
    /// to its own completed/failed outcome.
    /// </summary>
    private static HashSet<string> FindSupersededToolCallIds(IReadOnlyList<StoredChatMessage> messages)
    {
        var resultIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            if (message.Role != ChatRole.Tool)
            {
                continue;
            }

            var resultId = TryGetToolResultId(message.Content);
            if (!string.IsNullOrWhiteSpace(resultId))
            {
                resultIds.Add(resultId);
            }
        }

        return resultIds;
    }

    private static bool IsSupersededToolCall(
        StoredChatMessage message,
        HashSet<string> supersededToolCallIds)
    {
        if (message.Role != ChatRole.Tool)
        {
            return false;
        }

        var callId = TryGetToolCallId(message.Content);
        return !string.IsNullOrWhiteSpace(callId) && supersededToolCallIds.Contains(callId);
    }

    private static string? TryGetToolCallId(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.TryGetProperty("kind", out var kind) &&
                string.Equals(kind.GetString(), "tool_call", StringComparison.Ordinal) &&
                root.TryGetProperty("toolCallId", out var id) &&
                id.ValueKind == JsonValueKind.String)
            {
                return id.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string? TryGetToolResultId(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.TryGetProperty("toolCallId", out var id) && id.ValueKind == JsonValueKind.String)
            {
                return id.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static ChatStatus ToChatStatus(ConversationStatus status) => status switch
    {
        ConversationStatus.Active => ChatStatus.Ready,
        ConversationStatus.Completed => ChatStatus.Completed,
        ConversationStatus.Cancelled => ChatStatus.Cancelled,
        _ => ChatStatus.Failed
    };

    private static ChatMessageStatus ToMessageStatus(ConversationStatus status) => status switch
    {
        ConversationStatus.Cancelled => ChatMessageStatus.Cancelled,
        ConversationStatus.Error => ChatMessageStatus.Failed,
        _ => ChatMessageStatus.Completed
    };
}

/// <summary>
/// Terminal display status of a chat message item. Mirrors the subset of the WinUI
/// ChatMessageDisplayStatus needed to compute CanRetry/CanRegenerate: Streaming while a
/// response is in flight, Completed on success, Cancelled/Failed on an interrupted turn, and
/// Regenerated for an appended regenerate result (which is not itself further regenerable
/// until it completes another turn on top of it).
/// </summary>
public enum ChatMessageStatus
{
    Completed,
    Streaming,
    Cancelled,
    Failed,
    Regenerated
}

public sealed class ChatMessageItem : ObservableObject
{
    private string _content;
    private ChatMessageStatus _status;
    private bool _canRegenerate;

    public ChatMessageItem(ChatRole role, string content, ChatMessageStatus status = ChatMessageStatus.Completed)
    {
        Role = role;
        _content = content;
        _status = status;
    }

    public ChatRole Role { get; }

    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    public ChatMessageStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(CanRetry));
            }
        }
    }

    /// <summary>CanRetry = assistant message in a terminal Failed/Cancelled state.</summary>
    public bool CanRetry => Role == ChatRole.Assistant &&
        Status is ChatMessageStatus.Failed or ChatMessageStatus.Cancelled;

    /// <summary>
    /// CanRegenerate = this is the latest assistant message and it completed successfully.
    /// Set by AvaloniaChatViewModel.RefreshMessageEligibility; not computed locally because it
    /// depends on the message's position in the timeline.
    /// </summary>
    public bool CanRegenerate
    {
        get => _canRegenerate;
        private set => SetProperty(ref _canRegenerate, value);
    }

    internal void SetCanRegenerate(bool value) => CanRegenerate = value;
}
