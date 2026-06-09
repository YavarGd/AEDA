using System.IO;
using System.Net.Http;
using System.Text;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PersonalAI.Core.Chat;
using PersonalAI.Infrastructure.Chat;
using PersonalAI.Desktop.Windows;

namespace PersonalAI.Desktop;

public partial class MainWindow : Window
{
    private readonly IChatProvider _chatProvider;
    private readonly IConversationRepository _conversationRepository;
    private CancellationTokenSource? _generationCancellation;
    private Conversation? _activeConversation;
    private bool _isGenerating;
    private bool _isRefreshingConversationList;

    public bool AllowClose { get; set; }

    public MainWindow()
        : this(
            ChatProviderFactory.CreateDefaultLocalProvider(),
            ConversationRepositoryFactory.CreateDefaultRepository())
    {
    }

    public MainWindow(
        IChatProvider chatProvider,
        IConversationRepository conversationRepository)
    {
        ArgumentNullException.ThrowIfNull(chatProvider);
        ArgumentNullException.ThrowIfNull(conversationRepository);

        _chatProvider = chatProvider;
        _conversationRepository = conversationRepository;
        InitializeComponent();
        SetStatus(ChatUiStatus.Idle);
        UpdateGenerationControls(isGenerating: false);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshConversationListAsync();

        if (ConversationListBox.Items.Count == 0)
        {
            StartNewChat();
            return;
        }

        ConversationListBox.SelectedIndex = 0;
    }

    private void NewChatButton_Click(object sender, RoutedEventArgs e)
    {
        StartNewChat();
    }

    private async void ConversationListBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isRefreshingConversationList ||
            _isGenerating ||
            ConversationListBox.SelectedItem is not ConversationListItem item)
        {
            return;
        }

        await LoadConversationAsync(item.Id);
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isGenerating)
        {
            return;
        }

        var prompt = PromptTextBox.Text.Trim();
        var model = ModelTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(prompt))
        {
            SetStatus(ChatUiStatus.Error);
            ResponseTextBox.Text = "Enter a prompt before sending.";
            return;
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            SetStatus(ChatUiStatus.Error);
            ResponseTextBox.Text = "Enter a model name before sending.";
            return;
        }

        _generationCancellation = new CancellationTokenSource();
        var assistantResponse = new StringBuilder();

        try
        {
            UpdateGenerationControls(isGenerating: true);
            SetStatus(ChatUiStatus.Generating);

            var previousMessages = await GetActiveConversationMessagesAsync();
            var conversation = await EnsureActiveConversationAsync(prompt, model);
            var userMessage = CreateStoredMessage(
                conversation.Id,
                ChatRole.User,
                prompt);

            await _conversationRepository.AddMessageAsync(
                userMessage,
                _generationCancellation.Token);

            AppendHistoryMessage(userMessage);
            AppendAssistantHeader();
            PromptTextBox.Clear();

            await RefreshConversationListAsync(conversation.Id);

            var requestMessages = previousMessages
                .Select(message => new ChatMessage(message.Role, message.Content))
                .Append(new ChatMessage(ChatRole.User, prompt))
                .ToArray();

            var request = new ChatRequest(model, requestMessages);

            await foreach (var chunk in _chatProvider.StreamAsync(
                               request,
                               _generationCancellation.Token))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    assistantResponse.Append(chunk.Content);
                    ResponseTextBox.AppendText(chunk.Content);
                    ResponseTextBox.ScrollToEnd();
                }

                if (chunk.IsComplete)
                {
                    break;
                }
            }

            await SaveAssistantMessageIfNeededAsync(
                conversation.Id,
                assistantResponse.ToString());
            await UpdateActiveConversationStatusAsync(
                ConversationStatus.Completed,
                model);
            SetStatus(ChatUiStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            if (_activeConversation is not null)
            {
                await SaveAssistantMessageIfNeededAsync(
                    _activeConversation.Id,
                    assistantResponse.ToString());
                await UpdateActiveConversationStatusAsync(
                    ConversationStatus.Cancelled,
                    model);
            }

            SetStatus(ChatUiStatus.Cancelled);
        }
        catch (HttpRequestException exception)
        {
            await HandleGenerationErrorAsync(
                model,
                assistantResponse.ToString(),
                $"Could not connect to Ollama. {exception.Message}");
        }
        catch (InvalidDataException exception)
        {
            await HandleGenerationErrorAsync(
                model,
                assistantResponse.ToString(),
                exception.Message);
        }
        catch (Exception exception)
        {
            await HandleGenerationErrorAsync(
                model,
                assistantResponse.ToString(),
                exception.Message);
        }
        finally
        {
            var selectedConversationId = _activeConversation?.Id;
            _generationCancellation?.Dispose();
            _generationCancellation = null;
            UpdateGenerationControls(isGenerating: false);

            if (selectedConversationId is not null)
            {
                await RefreshConversationListAsync(selectedConversationId.Value);
            }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _generationCancellation?.Cancel();
    }

    public void ShowPaletteAndFocusPrompt()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (!IsVisible)
        {
            Show();
        }

        Topmost = true;
        PaletteWindowPlacementService.CenterOnActiveMonitor(this);
        WindowForegroundService.BringToForeground(this);
        FocusPromptInput();
    }

    public void HidePalette()
    {
        Hide();
    }

    public void FocusPromptInput()
    {
        PromptTextBox.Focus();
        PromptTextBox.CaretIndex = PromptTextBox.Text.Length;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
        {
            return;
        }

        e.Cancel = true;
        HidePalette();
    }

    private void MainWindow_KeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HidePalette();
            e.Handled = true;
        }
    }

    private void StartNewChat()
    {
        _activeConversation = null;
        _isRefreshingConversationList = true;
        ConversationListBox.SelectedItem = null;
        _isRefreshingConversationList = false;
        PromptTextBox.Clear();
        ResponseTextBox.Clear();
        ModelTextBox.Text = "gemma4";
        SetStatus(ChatUiStatus.Idle);
    }

    private async Task LoadConversationAsync(Guid conversationId)
    {
        var conversation = await _conversationRepository.GetConversationAsync(
            conversationId);

        if (conversation is null)
        {
            StartNewChat();
            await RefreshConversationListAsync();
            return;
        }

        _activeConversation = conversation;
        ModelTextBox.Text = conversation.Model;

        var messages = await _conversationRepository.ListMessagesAsync(
            conversation.Id);

        ResponseTextBox.Text = FormatHistory(messages);
        ResponseTextBox.ScrollToEnd();
        SetStatus(MapConversationStatus(conversation.Status));
    }

    private async Task<IReadOnlyList<StoredChatMessage>>
        GetActiveConversationMessagesAsync()
    {
        if (_activeConversation is null)
        {
            return [];
        }

        return await _conversationRepository.ListMessagesAsync(
            _activeConversation.Id);
    }

    private async Task<Conversation> EnsureActiveConversationAsync(
        string prompt,
        string model)
    {
        var now = DateTimeOffset.UtcNow;

        if (_activeConversation is null)
        {
            var conversation = new Conversation(
                Guid.NewGuid(),
                ConversationTitleGenerator.CreateTitle(prompt),
                model,
                now,
                now,
                ConversationStatus.Active);

            _activeConversation =
                await _conversationRepository.CreateConversationAsync(conversation);
            return _activeConversation;
        }

        _activeConversation = _activeConversation with
        {
            Model = model,
            UpdatedAtUtc = now,
            Status = ConversationStatus.Active
        };

        await _conversationRepository.UpdateConversationAsync(_activeConversation);
        return _activeConversation;
    }

    private async Task UpdateActiveConversationStatusAsync(
        ConversationStatus status,
        string model)
    {
        if (_activeConversation is null)
        {
            return;
        }

        _activeConversation = _activeConversation with
        {
            Model = model,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Status = status
        };

        await _conversationRepository.UpdateConversationAsync(_activeConversation);
    }

    private async Task SaveAssistantMessageIfNeededAsync(
        Guid conversationId,
        string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        await _conversationRepository.AddMessageAsync(
            CreateStoredMessage(conversationId, ChatRole.Assistant, content));
    }

    private async Task HandleGenerationErrorAsync(
        string model,
        string partialAssistantResponse,
        string message)
    {
        if (_activeConversation is not null)
        {
            await SaveAssistantMessageIfNeededAsync(
                _activeConversation.Id,
                partialAssistantResponse);
            await UpdateActiveConversationStatusAsync(
                ConversationStatus.Error,
                model);
        }

        SetStatus(ChatUiStatus.Error);

        if (!string.IsNullOrEmpty(partialAssistantResponse))
        {
            ResponseTextBox.AppendText($"{Environment.NewLine}{Environment.NewLine}");
        }

        ResponseTextBox.AppendText(message);
        ResponseTextBox.ScrollToEnd();
    }

    private async Task RefreshConversationListAsync(Guid? selectedConversationId = null)
    {
        var conversations = await _conversationRepository.ListConversationsAsync();
        var selectedId = selectedConversationId ?? _activeConversation?.Id;
        var items = conversations
            .Select(conversation => new ConversationListItem(
                conversation.Id,
                conversation.Title))
            .ToArray();

        _isRefreshingConversationList = true;
        ConversationListBox.ItemsSource = items;
        ConversationListBox.SelectedItem = items.FirstOrDefault(
            item => item.Id == selectedId);
        _isRefreshingConversationList = false;
    }

    private void AppendHistoryMessage(StoredChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(ResponseTextBox.Text))
        {
            ResponseTextBox.AppendText(
                $"{Environment.NewLine}{Environment.NewLine}");
        }

        ResponseTextBox.AppendText(
            $"{GetRoleLabel(message.Role)}:{Environment.NewLine}{message.Content}");
        ResponseTextBox.ScrollToEnd();
    }

    private void AppendAssistantHeader()
    {
        ResponseTextBox.AppendText(
            $"{Environment.NewLine}{Environment.NewLine}Assistant:{Environment.NewLine}");
        ResponseTextBox.ScrollToEnd();
    }

    private static StoredChatMessage CreateStoredMessage(
        Guid conversationId,
        ChatRole role,
        string content)
    {
        return new StoredChatMessage(
            Guid.NewGuid(),
            conversationId,
            role,
            content,
            DateTimeOffset.UtcNow);
    }

    private static string FormatHistory(IEnumerable<StoredChatMessage> messages)
    {
        var builder = new StringBuilder();

        foreach (var message in messages)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.AppendLine($"{GetRoleLabel(message.Role)}:");
            builder.Append(message.Content);
        }

        return builder.ToString();
    }

    private static string GetRoleLabel(ChatRole role)
    {
        return role switch
        {
            ChatRole.User => "User",
            ChatRole.Assistant => "Assistant",
            ChatRole.System => "System",
            ChatRole.Tool => "Tool",
            _ => role.ToString()
        };
    }

    private static ChatUiStatus MapConversationStatus(ConversationStatus status)
    {
        return status switch
        {
            ConversationStatus.Active => ChatUiStatus.Idle,
            ConversationStatus.Completed => ChatUiStatus.Completed,
            ConversationStatus.Cancelled => ChatUiStatus.Cancelled,
            ConversationStatus.Error => ChatUiStatus.Error,
            _ => ChatUiStatus.Idle
        };
    }

    private void UpdateGenerationControls(bool isGenerating)
    {
        _isGenerating = isGenerating;
        SendButton.IsEnabled = !isGenerating;
        CancelButton.IsEnabled = isGenerating;
        NewChatButton.IsEnabled = !isGenerating;
        ConversationListBox.IsEnabled = !isGenerating;
    }

    private void SetStatus(ChatUiStatus status)
    {
        StatusTextBlock.Text = status.ToString();
    }

    private sealed record ConversationListItem(Guid Id, string Title);
}
