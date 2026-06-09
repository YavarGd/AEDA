using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Context;
using PersonalAI.Core.Editor;
using PersonalAI.Infrastructure.Chat;
using PersonalAI.Infrastructure.Context;
using PersonalAI.Desktop.Windows;

namespace PersonalAI.Desktop;

public partial class MainWindow : Window
{
    private readonly IChatProvider _chatProvider;
    private readonly IConversationRepository _conversationRepository;
    private readonly IActiveContextProvider _activeContextProvider;
    private readonly ForegroundWindowTracker _foregroundWindowTracker;
    private readonly WindowPositionService _windowPositionService;
    private readonly AttachedContextState _attachedContextState = new();
    private EditorContextEnvelope? _attachedEditorContext;
    private CancellationTokenSource? _generationCancellation;
    private Conversation? _activeConversation;
    private ActiveWindowReference? _lastExternalWindow;
    private ResponseAutoScrollController _responseAutoScrollController = null!;
    private bool _isGenerating;
    private bool _isRefreshingConversationList;

    public bool AllowClose { get; set; }

    public MainWindow()
        : this(
            ChatProviderFactory.CreateDefaultLocalProvider(),
            ConversationRepositoryFactory.CreateDefaultRepository(),
            ActiveContextProviderFactory.CreateDefaultProvider(),
            new ForegroundWindowTracker(),
            new WindowPositionService())
    {
    }

    public MainWindow(
        IChatProvider chatProvider,
        IConversationRepository conversationRepository,
        IActiveContextProvider activeContextProvider,
        ForegroundWindowTracker foregroundWindowTracker,
        WindowPositionService windowPositionService)
    {
        ArgumentNullException.ThrowIfNull(chatProvider);
        ArgumentNullException.ThrowIfNull(conversationRepository);
        ArgumentNullException.ThrowIfNull(activeContextProvider);
        ArgumentNullException.ThrowIfNull(foregroundWindowTracker);
        ArgumentNullException.ThrowIfNull(windowPositionService);

        _chatProvider = chatProvider;
        _conversationRepository = conversationRepository;
        _activeContextProvider = activeContextProvider;
        _foregroundWindowTracker = foregroundWindowTracker;
        _windowPositionService = windowPositionService;
        InitializeComponent();
        _responseAutoScrollController = new ResponseAutoScrollController(ResponseTextBox);
        SetStatus(ChatUiStatus.Idle);
        UpdateContextPreview();
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
                .ToArray();

            var composedMessages = ContextPromptComposer.Compose(
                requestMessages,
                prompt,
                _attachedContextState.Current);
            composedMessages = ComposeWithEditorContext(
                composedMessages,
                _attachedEditorContext);
            var request = new ChatRequest(model, composedMessages);

            await foreach (var chunk in _chatProvider.StreamAsync(
                               request,
                               _generationCancellation.Token))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    assistantResponse.Append(chunk.Content);
                    ResponseTextBox.AppendText(chunk.Content);
                    _responseAutoScrollController.ScrollToEndAsync();
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

    public void CaptureExternalWindowBeforeActivation()
    {
        _lastExternalWindow =
            _foregroundWindowTracker.CaptureCurrentExternalWindow(this) ??
            _lastExternalWindow;
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
        _windowPositionService.PlaceWindow(this);
        WindowForegroundService.BringToForeground(this);
        FocusPromptInput();
    }

    public void HidePalette()
    {
        Hide();
    }

    private void DragHeader_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
            _windowPositionService.RememberPosition(this);
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void ClearAttachedContext()
    {
        RemoveAttachedContext();
        _attachedEditorContext = null;
        UpdateContextPreview();
    }

    public void AttachEditorContext(EditorContextEnvelope envelope)
    {
        _attachedEditorContext = envelope;

        if (string.IsNullOrWhiteSpace(PromptTextBox.Text) &&
            !string.IsNullOrWhiteSpace(envelope.UserPrompt))
        {
            PromptTextBox.Text = envelope.UserPrompt;
            FocusPromptInput();
        }

        UpdateContextPreview();
        SetStatus(ChatUiStatus.Idle);
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
        RemoveAttachedContext();
        _attachedEditorContext = null;
        _isRefreshingConversationList = true;
        ConversationListBox.SelectedItem = null;
        _isRefreshingConversationList = false;
        PromptTextBox.Clear();
        ResponseTextBox.Clear();
        ModelTextBox.Text = "gemma4";
        SetStatus(ChatUiStatus.Idle);
    }

    private async void AttachContextButton_Click(object sender, RoutedEventArgs e)
    {
        await CaptureAndAttachContextAsync(
            selectedText: null,
            captureScreenshot: false);
    }

    private async void UseClipboardTextButton_Click(object sender, RoutedEventArgs e)
    {
        var clipboardText = TryGetClipboardText();

        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            SetStatus(ChatUiStatus.Error);
            ContextPreviewTextBlock.Text =
                "Clipboard text was unavailable. Copy text first, then choose Use Clipboard Text.";
            ContextPreviewBorder.Visibility = Visibility.Visible;
            return;
        }

        await CaptureAndAttachContextAsync(
            selectedText: clipboardText,
            captureScreenshot: false);
    }

    private async void CaptureScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        await CaptureAndAttachContextAsync(
            selectedText: _attachedContextState.Current?.CapturedSelectedText,
            captureScreenshot: true);
    }

    private void RemoveContextButton_Click(object sender, RoutedEventArgs e)
    {
        ClearAttachedContext();
    }

    private async Task CaptureAndAttachContextAsync(
        string? selectedText,
        bool captureScreenshot)
    {
        try
        {
            var context = await _activeContextProvider.CaptureAsync(
                new ContextCaptureRequest(
                    GetValidExternalWindowHandle(),
                    selectedText,
                    captureScreenshot));

            if (context is null)
            {
                SetStatus(ChatUiStatus.Error);
                ContextPreviewTextBlock.Text =
                    "No valid external window was available. Open the target app, press Ctrl+Alt+Space, then attach context.";
                ContextPreviewBorder.Visibility = Visibility.Visible;
                return;
            }

            RemoveAttachedContext();
            _attachedContextState.Attach(context);
            UpdateContextPreview();
            SetStatus(ChatUiStatus.Idle);
        }
        catch (Exception exception)
        {
            SetStatus(ChatUiStatus.Error);
            ContextPreviewTextBlock.Text =
                $"Could not attach context. {exception.Message}";
            ContextPreviewBorder.Visibility = Visibility.Visible;
        }
    }

    private nint? GetValidExternalWindowHandle()
    {
        var snapshot = _foregroundWindowTracker.GetLastValidExternalWindow(this);
        _lastExternalWindow = snapshot ?? _lastExternalWindow;

        return snapshot?.WindowHandle;
    }

    private static string? TryGetClipboardText()
    {
        try
        {
            return System.Windows.Clipboard.ContainsText()
                ? System.Windows.Clipboard.GetText()
                : null;
        }
        catch (Exception exception) when (
            exception is ExternalException ||
            exception is ThreadStateException)
        {
            return null;
        }
    }

    private void RemoveAttachedContext()
    {
        var removed = _attachedContextState.Remove();
        DeleteScreenshotIfPresent(removed);
    }

    private void UpdateContextPreview()
    {
        var context = _attachedContextState.Current;

        if (context is null && _attachedEditorContext is null)
        {
            ContextPreviewTextBlock.Text = string.Empty;
            ContextPreviewBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var preview = new StringBuilder();

        if (context is not null)
        {
            preview.Append(ContextFormatter.FormatPreview(context));
        }

        if (_attachedEditorContext is not null)
        {
            if (preview.Length > 0)
            {
                preview.AppendLine();
                preview.AppendLine();
            }

            preview.Append(EditorContextPromptComposer.FormatPreview(
                _attachedEditorContext));
        }

        ContextPreviewTextBlock.Text = preview.ToString();
        ContextPreviewBorder.Visibility = Visibility.Visible;
    }

    private static void DeleteScreenshotIfPresent(
        ActiveApplicationContext? context)
    {
        if (string.IsNullOrWhiteSpace(context?.ScreenshotPath))
        {
            return;
        }

        try
        {
            if (File.Exists(context.ScreenshotPath))
            {
                File.Delete(context.ScreenshotPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
        _responseAutoScrollController?.ScrollToEndAsync();
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
        _responseAutoScrollController?.ScrollToEndAsync();
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
        _responseAutoScrollController?.ScrollToEndAsync();
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

    private static IReadOnlyList<ChatMessage> ComposeWithEditorContext(
        IReadOnlyList<ChatMessage> messages,
        EditorContextEnvelope? editorContext)
    {
        if (editorContext is null)
        {
            return messages;
        }

        var composed = messages.ToList();
        var userIndex = composed.FindLastIndex(message => message.Role == ChatRole.User);
        var editorMessage = new ChatMessage(
            ChatRole.System,
            EditorContextPromptComposer.FormatPromptBlock(editorContext));

        if (userIndex < 0)
        {
            composed.Add(editorMessage);
        }
        else
        {
            composed.Insert(userIndex, editorMessage);
        }

        return composed;
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
        AttachContextButton.IsEnabled = !isGenerating;
        UseClipboardTextButton.IsEnabled = !isGenerating;
        CaptureScreenshotButton.IsEnabled = !isGenerating;
    }

    private void SetStatus(ChatUiStatus status)
    {
        StatusTextBlock.Text = status.ToString();
    }

    private sealed record ConversationListItem(Guid Id, string Title);
}
