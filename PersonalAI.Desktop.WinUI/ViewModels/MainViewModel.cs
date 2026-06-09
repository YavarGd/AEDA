using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using PersonalAI.Core.Chat;
using PersonalAI.Desktop.WinUI.Models;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ChatSessionService _chatSession;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _generationCancellation;
    private Guid _activeConversationId;

    public MainViewModel(ChatSessionService chatSession)
    {
        _chatSession = chatSession;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        NewChat();
    }

    public ObservableCollection<ConversationListItem> Conversations { get; } = [];

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public IReadOnlyList<string> AvailableModels { get; } = ["gemma4"];

    [ObservableProperty]
    private string _selectedModel = "gemma4";

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _conversationSearch = string.Empty;

    [ObservableProperty]
    private string _currentConversationTitle = "New chat";

    [ObservableProperty]
    private ChatStatus _status = ChatStatus.Ready;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public bool IsGenerating => Status is ChatStatus.Connecting or ChatStatus.Generating;

    public bool CanSend => !IsGenerating && !string.IsNullOrWhiteSpace(Prompt);

    partial void OnPromptChanged(string value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnStatusChanged(ChatStatus value)
    {
        OnPropertyChanged(nameof(IsGenerating));
        OnPropertyChanged(nameof(CanSend));
        SendMessageCommand.NotifyCanExecuteChanged();
        CancelGenerationCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void NewChat()
    {
        _generationCancellation?.Cancel();
        _activeConversationId = Guid.NewGuid();
        Messages.Clear();
        Prompt = string.Empty;
        CurrentConversationTitle = "New chat";
        Status = ChatStatus.Ready;
        StatusMessage = "Ready";

        Conversations.Insert(0, new ConversationListItem(
            _activeConversationId,
            CurrentConversationTitle,
            "Empty conversation"));
    }

    [RelayCommand]
    public void SelectConversation(ConversationListItem? conversation)
    {
        if (conversation is null || conversation.Id == _activeConversationId)
        {
            return;
        }

        _activeConversationId = conversation.Id;
        CurrentConversationTitle = conversation.Title;
        Messages.Clear();
        Status = ChatStatus.Ready;
        StatusMessage = "Ready";
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task SendMessageAsync()
    {
        var prompt = Prompt.Trim();

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        _generationCancellation = new CancellationTokenSource();
        Status = ChatStatus.Connecting;
        StatusMessage = "Connecting";
        Prompt = string.Empty;

        AddUserMessage(prompt);
        var assistantMessage = new ChatMessageViewModel(ChatRole.Assistant, string.Empty);
        Messages.Add(assistantMessage);

        try
        {
            Status = ChatStatus.Generating;
            StatusMessage = "Generating";

            var history = Messages
                .Where(message => message != assistantMessage)
                .Select(message => new ChatMessage(message.Role, message.Content))
                .ToArray();

            await foreach (var chunk in _chatSession.StreamAsync(
                               SelectedModel,
                               history,
                               _generationCancellation.Token))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    AppendAssistantText(assistantMessage, chunk.Content);
                }
            }

            Status = ChatStatus.Completed;
            StatusMessage = "Completed";
        }
        catch (OperationCanceledException)
        {
            Status = ChatStatus.Cancelled;
            StatusMessage = "Cancelled";
        }
        catch (Exception exception)
        {
            Status = ChatStatus.Failed;
            StatusMessage = $"Failed: {exception.Message}";
        }
        finally
        {
            _generationCancellation.Dispose();
            _generationCancellation = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsGenerating))]
    public void CancelGeneration()
    {
        _generationCancellation?.Cancel();
    }

    [RelayCommand]
    public void AttachApplicationContext()
    {
        StatusMessage = "Application context migration is not part of this first WinUI slice.";
    }

    [RelayCommand]
    public void AttachClipboardText()
    {
        StatusMessage = "Clipboard context migration is not part of this first WinUI slice.";
    }

    [RelayCommand]
    public void CaptureScreenshot()
    {
        StatusMessage = "Screenshot context migration is not part of this first WinUI slice.";
    }

    [RelayCommand]
    public void RemoveAttachedContext()
    {
        StatusMessage = "No attached context in this first WinUI slice.";
    }

    private void AddUserMessage(string content)
    {
        Messages.Add(new ChatMessageViewModel(ChatRole.User, content));

        if (CurrentConversationTitle == "New chat")
        {
            CurrentConversationTitle = ConversationTitleGenerator.CreateTitle(content);
            Conversations[0] = Conversations[0] with
            {
                Title = CurrentConversationTitle,
                Preview = content
            };
        }
    }

    private void AppendAssistantText(
        ChatMessageViewModel assistantMessage,
        string content)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            assistantMessage.Content += content;
        });
    }
}
