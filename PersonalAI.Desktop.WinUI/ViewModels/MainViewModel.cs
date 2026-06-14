using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Context;
using PersonalAI.Core.Editor;
using PersonalAI.Core.Settings;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Reference;
using PersonalAI.Core.Tools.Workspace;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Tools.Workspace;
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
    private readonly IApplicationSettingsService _settingsService;
    private readonly IChatModelRouter _modelRouter;
    private readonly ITypedToolRuntime _toolRuntime;
    private readonly IWorkspaceRegistry _workspaceRegistry;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> _listModelsAsync;
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
        ScreenshotAttachmentService screenshotAttachmentService,
        IApplicationSettingsService settingsService,
        SettingsViewModel settings,
        IChatModelRouter modelRouter,
        ITypedToolRuntime toolRuntime,
        TaskTimelineViewModel taskTimeline,
        IWorkspaceRegistry workspaceRegistry,
        Func<CancellationToken, Task<IReadOnlyList<string>>> listModelsAsync)
    {
        _conversationSession = conversationSession;
        _clipboardContextService = clipboardContextService;
        _activeWindowContextService = activeWindowContextService;
        _screenshotAttachmentService = screenshotAttachmentService;
        _settingsService = settingsService;
        Settings = settings;
        _modelRouter = modelRouter;
        _toolRuntime = toolRuntime;
        TaskTimeline = taskTimeline;
        _workspaceRegistry = workspaceRegistry;
        _listModelsAsync = listModelsAsync;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        ApplySettings(settingsService.Current);
    }

    public ObservableCollection<ConversationListItem> Conversations { get; } = [];

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<AttachedContextItem> AttachedContexts { get; } = [];

    public SettingsViewModel Settings { get; }

    public TaskTimelineViewModel TaskTimeline { get; }

    public Func<GenerationStopConfirmationRequest, Task<bool>> ConfirmStopGenerationAsync { get; set; } =
        _ => Task.FromResult(false);

    public Func<Task<bool>> ConfirmClearAllContextsAsync { get; set; } =
        () => Task.FromResult(true);

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

    [ObservableProperty]
    private string _routingStatusMessage = "Model will be selected automatically.";

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private GridLength _sidebarColumnWidth = new(280);

    [ObservableProperty]
    private Thickness _sidebarPadding = new(16);

    [ObservableProperty]
    private bool _showConversationMetadata = true;

    [ObservableProperty]
    private bool _showMessageMetadata = true;

    [ObservableProperty]
    private string _developerWorkspaceRoot = string.Empty;

    [ObservableProperty]
    private string _developerWorkspaceId = string.Empty;

    [ObservableProperty]
    private string _developerToolPath = ".";

    [ObservableProperty]
    private string _developerSearchQuery = string.Empty;

    [ObservableProperty]
    private string _developerToolResult = "No workspace tool run yet.";

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
        false;

    public bool IsDeveloperDiagnosticsVisible
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var conversations = await _conversationSession.LoadConversationsAsync(
            cancellationToken);

        _allConversations.Clear();
        _allConversations.AddRange(conversations);
        ApplyConversationFilter();

        if (_allConversations.Count > 0 &&
            _settingsService.Current.General.LaunchDestination ==
            LaunchDestination.LastConversation)
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

    public void ApplySettings(ApplicationSettings settings)
    {
        var normalized = ApplicationSettingsValidator.Normalize(settings);
        SidebarColumnWidth = new GridLength(
            normalized.Appearance.CompactSidebar ? 180 : 280);
        SidebarPadding = new Thickness(
            normalized.Appearance.CompactSidebar ? 10 : 16);
        ShowConversationMetadata = !normalized.Appearance.CompactSidebar;
        ShowMessageMetadata = normalized.Appearance.ShowMessageMetadata;
        OnPropertyChanged(nameof(HasUnsupportedImageContext));
        OnPropertyChanged(nameof(CanSend));
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
    public void OpenSettings()
    {
        IsSettingsOpen = true;
    }

    [RelayCommand]
    public void CloseSettings()
    {
        IsSettingsOpen = false;
    }

    [RelayCommand]
    public async Task RunReferenceToolAsync()
    {
        var taskId = TaskId.NewId();
        TaskTimeline.ObserveTask(taskId);
        StatusMessage = "Running reference tool...";

        var result = await _toolRuntime.InvokeAsync(
            taskId,
            new ToolInvocation(
                GetCurrentUtcTimeTool.Id,
                new GetCurrentUtcTimeInput()),
            CancellationToken.None);

        if (result is
            {
                IsSuccess: true,
                Output: GetCurrentUtcTimeOutput output
            })
        {
            Status = ChatStatus.Completed;
            StatusMessage = $"UTC time: {output.Iso8601}";
            return;
        }

        Status = result.Status == ToolExecutionStatus.Cancelled
            ? ChatStatus.Cancelled
            : ChatStatus.Failed;
        StatusMessage = result.SafeErrorMessage ?? result.Summary;
    }

    private async Task RunDeveloperToolAsync(ToolInvocation invocation)
    {
        if (string.IsNullOrWhiteSpace(DeveloperWorkspaceId))
        {
            DeveloperToolResult = "Register a developer workspace first.";
            return;
        }

        var taskId = TaskId.NewId();
        TaskTimeline.ObserveTask(taskId);
        var result = await _toolRuntime.InvokeAsync(
            taskId,
            invocation,
            CancellationToken.None);

        DeveloperToolResult = FormatDeveloperToolResult(result);
        StatusMessage = result.IsSuccess
            ? "Workspace diagnostic completed."
            : result.SafeErrorMessage ?? result.Summary;
    }

    private static string FormatDeveloperToolResult(ToolResult result)
    {
        if (!result.IsSuccess)
        {
            return $"{result.Status}: {result.SafeErrorMessage ?? result.Summary}";
        }

        return result.Output switch
        {
            GetWorkspaceInfoOutput output =>
                $"{output.DisplayName} ({output.WorkspaceId}) files={output.ImmediateFileCount} dirs={output.ImmediateDirectoryCount}",
            ListDirectoryOutput output =>
                string.Join(Environment.NewLine, output.Entries.Select(entry =>
                    $"{entry.Type} {entry.RelativePath}")),
            ReadTextFileOutput output =>
                output.IsTruncated ? output.Content + Environment.NewLine + "[truncated]" : output.Content,
            SearchWorkspaceTextOutput output =>
                string.Join(Environment.NewLine, output.Matches.Select(match =>
                    $"{match.RelativeFilePath}:{match.LineNumber}:{match.LinePreview}")),
            _ => result.Summary
        };
    }

    [RelayCommand]
    public void RegisterDeveloperWorkspace()
    {
        DeveloperToolResult =
            "Register workspaces in Settings, then paste the workspace ID here for diagnostics.";
    }

    [RelayCommand]
    public async Task RunDeveloperListDirectoryAsync()
    {
        await RunDeveloperToolAsync(new ToolInvocation(
            ListDirectoryTool.Id,
            new ListDirectoryInput(
                new WorkspaceId(DeveloperWorkspaceId),
                DeveloperToolPath,
                MaxEntries: 25)));
    }

    [RelayCommand]
    public async Task RunDeveloperReadFileAsync()
    {
        await RunDeveloperToolAsync(new ToolInvocation(
            ReadTextFileTool.Id,
            new ReadTextFileInput(
                new WorkspaceId(DeveloperWorkspaceId),
                DeveloperToolPath,
                MaxCharacters: 4000)));
    }

    [RelayCommand]
    public async Task RunDeveloperSearchWorkspaceAsync()
    {
        await RunDeveloperToolAsync(new ToolInvocation(
            SearchWorkspaceTextTool.Id,
            new SearchWorkspaceTextInput(
                new WorkspaceId(DeveloperWorkspaceId),
                DeveloperSearchQuery,
                DeveloperToolPath,
                MaxResults: 25)));
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
        CurrentConversationTitle = storedConversation.Title;
        Messages.Clear();

        foreach (var message in messages)
        {
            Messages.Add(new ChatMessageViewModel(
                message.Role,
                FormatStoredMessage(message)));
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
        IReadOnlyList<AttachedContextItem> contextSnapshot,
        CancellationToken cancellationToken)
    {
        ChatMessageViewModel? assistantMessage = null;
        var assistantResponse = new StringBuilder();
        var requestAccepted = false;
        var model = ModelRoutingSettings.DefaultModel;

        try
        {
            var previousHistory = Messages
                .Select(message => new ChatMessage(message.Role, message.Content))
                .ToArray();
            var routingDecision = await SelectModelAsync(
                prompt,
                contextSnapshot,
                cancellationToken);
            RoutingStatusMessage = routingDecision.UserVisibleReason;
            model = routingDecision.SelectedModel;
            var routedPrompt = routingDecision.RoutedPrompt.Trim();

            if (string.IsNullOrWhiteSpace(routedPrompt))
            {
                routedPrompt = prompt;
            }

            var conversation = await EnsureActiveConversationAsync(
                routedPrompt,
                model,
                CancellationToken.None);

            await _conversationSession.AddMessageAsync(
                conversation.Id,
                ChatRole.User,
                routedPrompt,
                CancellationToken.None);

            AddMessage(new ChatMessageViewModel(ChatRole.User, routedPrompt));
            assistantMessage = new ChatMessageViewModel(ChatRole.Assistant, string.Empty);
            AddMessage(assistantMessage);

            Status = ChatStatus.Generating;
            StatusMessage = "Generating";

            var requestMessages = AttachedContextPromptComposer.Compose(
                previousHistory,
                routedPrompt,
                contextSnapshot,
                _settingsService.Current.Context.MaxIndividualClipboardCharacters,
                _settingsService.Current.Context.MaxTotalTextContextCharacters);
            var toolTaskId = TaskId.NewId();
            var canUseWorkspaceTools =
                _conversationSession.CanUseWorkspaceTools(model);

            if (canUseWorkspaceTools)
            {
                TaskTimeline.ObserveTask(toolTaskId);
                StatusMessage = "Workspace tools available.";
            }
            else if (LooksLikeWorkspaceAccessRequest(routedPrompt) &&
                     _workspaceRegistry.List().Count > 0)
            {
                StatusMessage = "This model does not support workspace tools.";
            }

            await foreach (var chunk in _conversationSession.StreamWithWorkspaceToolsAsync(
                               conversation.Id,
                               toolTaskId,
                               model,
                               requestMessages,
                               cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(chunk.ActivityMessage))
                {
                    AddMessage(new ChatMessageViewModel(
                        ChatRole.Tool,
                        chunk.ActivityMessage));
                    StatusMessage = chunk.ActivityMessage;
                }

                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    if (!requestAccepted)
                    {
                        requestAccepted = true;
                        ClearAttachedContextItemsAfterSend();
                    }

                    assistantResponse.Append(chunk.Content);
                    AppendAssistantText(assistantMessage, chunk.Content, conversation.Id);
                }
            }

            if (!requestAccepted)
            {
                ClearAttachedContextItemsAfterSend();
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
    public async Task ClearAllContextsAsync()
    {
        if (_settingsService.Current.General.ConfirmBeforeClearingAllContext &&
            !await ConfirmClearAllContextsAsync())
        {
            return;
        }

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
        if (AttachedContexts.Count >=
            _settingsService.Current.Context.MaxAttachedContextItems)
        {
            SetContextStatus(
                $"Remove an attached context before adding another. Limit: {_settingsService.Current.Context.MaxAttachedContextItems}.");
            return;
        }

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

    private void ClearAttachedContextItemsAfterSend()
    {
        if (_settingsService.Current.Context.ClearAttachmentsAfterSuccessfulSend)
        {
            ClearAttachedContextItems();
        }
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
                "No configured vision model can accept the attached screenshot.");
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

    private static string FormatStoredMessage(StoredChatMessage message)
    {
        if (message.Role != ChatRole.Tool)
        {
            return message.Content;
        }

        try
        {
            using var document = JsonDocument.Parse(message.Content);
            var root = document.RootElement;

            if (root.TryGetProperty("kind", out var kind) &&
                string.Equals(kind.GetString(), "tool_call", StringComparison.Ordinal))
            {
                var toolName = root.TryGetProperty("toolName", out var tool)
                    ? tool.GetString()
                    : "workspace tool";
                return $"Requested workspace tool: {toolName}.";
            }

            if (root.TryGetProperty("status", out var status))
            {
                var toolName = root.TryGetProperty("toolName", out var tool)
                    ? tool.GetString()
                    : "workspace tool";
                var safeError = root.TryGetProperty("safeErrorMessage", out var error)
                    ? error.GetString()
                    : null;
                var isTruncated = root.TryGetProperty("isTruncated", out var truncated) &&
                    truncated.ValueKind == JsonValueKind.True;

                if (!string.IsNullOrWhiteSpace(safeError))
                {
                    return safeError;
                }

                return isTruncated
                    ? $"Workspace tool completed with truncated results: {toolName}."
                    : $"Workspace tool {status.GetString()?.ToLowerInvariant()}: {toolName}.";
            }
        }
        catch (JsonException)
        {
        }

        return "Workspace tool activity.";
    }

    private static bool LooksLikeWorkspaceAccessRequest(string prompt)
    {
        var value = prompt.ToLowerInvariant();
        return value.Contains("workspace", StringComparison.Ordinal) ||
            value.Contains("readme", StringComparison.Ordinal) ||
            value.Contains("list files", StringComparison.Ordinal) ||
            value.Contains("search", StringComparison.Ordinal) ||
            value.Contains("open ", StringComparison.Ordinal);
    }

    private void AddMessage(ChatMessageViewModel message)
    {
        Messages.Add(message);
        NotifyMessageCollectionChanged();
    }

    private async Task<ModelRoutingDecision> SelectModelAsync(
        string prompt,
        IReadOnlyList<AttachedContextItem> contextSnapshot,
        CancellationToken cancellationToken)
    {
        var installedModels = await GetInstalledModelsForRoutingAsync(
            cancellationToken);
        var request = new ModelRoutingRequest(
            prompt,
            contextSnapshot
                .Select(context => new AttachedContextSignal(
                    context.Type.ToString(),
                    context.Images.Count > 0))
                .ToArray(),
            installedModels,
            _settingsService.Current.Models.Assignments);

        var decision = await _modelRouter.SelectModelAsync(
            request,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(decision.FallbackReason))
        {
            StatusMessage = decision.FallbackReason;
        }

        return decision;
    }

    private async Task<IReadOnlyList<string>> GetInstalledModelsForRoutingAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var models = await _listModelsAsync(cancellationToken);

            if (models.Count > 0)
            {
                return models;
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException ||
            exception is InvalidOperationException ||
            exception is TaskCanceledException)
        {
        }

        return _settingsService.Current.Models.Assignments
            .Select(assignment => assignment.Model)
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
