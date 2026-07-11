using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Context;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Editor;
using PersonalAI.Core.Modules;
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
    private readonly IAedaModuleRegistry _moduleRegistry;
    private readonly IModuleSuggestionService _moduleSuggestionService;
    private readonly IWorkspaceRegistry _workspaceRegistry;
    private readonly IClipboardWriter _clipboardWriter;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> _listModelsAsync;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly GenerationNavigationGuard _navigationGuard = new();
    private readonly AttachedContextCollection _attachedContextCollection = new();
    private readonly List<Conversation> _allConversations = [];
    private readonly Dictionary<Guid, string> _conversationPreviews = new();
    private readonly Dictionary<Guid, string> _conversationModelOverrides = new();
    private CancellationTokenSource? _generationCancellation;
    private Conversation? _activeConversation;
    private Task? _activeGenerationTask;
    private string? _draftConversationModelOverride;
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
        IAedaModuleRegistry moduleRegistry,
        IModuleSuggestionService moduleSuggestionService,
        AedaModuleDashboardViewModel moduleDashboard,
        AedaTaskCenterViewModel taskCenter,
        AedaCodeModuleViewModel aedaCode,
        AedaMemoryModuleViewModel aedaMemory,
        AedaResearchModuleViewModel aedaResearch,
        TaskTimelineViewModel taskTimeline,
        IWorkspaceRegistry workspaceRegistry,
        IClipboardWriter clipboardWriter,
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
        _moduleRegistry = moduleRegistry;
        _moduleSuggestionService = moduleSuggestionService;
        ModuleDashboard = moduleDashboard;
        TaskCenter = taskCenter;
        AedaCode = aedaCode;
        AedaMemory = aedaMemory;
        AedaResearch = aedaResearch;
        TaskTimeline = taskTimeline;
        _workspaceRegistry = workspaceRegistry;
        _clipboardWriter = clipboardWriter;
        _listModelsAsync = listModelsAsync;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        ApplySettings(settingsService.Current);
    }

    public ObservableCollection<ConversationListItem> Conversations { get; } = [];

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<AttachedContextItem> AttachedContexts { get; } = [];

    public SettingsViewModel Settings { get; }

    public AedaShellNavigationState ShellNavigation { get; } = new();

    public AedaModuleDashboardViewModel ModuleDashboard { get; }

    public AedaTaskCenterViewModel TaskCenter { get; }

    public AedaCodeModuleViewModel AedaCode { get; }

    public AedaMemoryModuleViewModel AedaMemory { get; }

    public AedaResearchModuleViewModel AedaResearch { get; }

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
    private string _contextStatusMessage = string.Empty;

    [ObservableProperty]
    private string _routingStatusMessage = "Model will be selected automatically.";

    [ObservableProperty]
    private ModuleSuggestion? _moduleSuggestion;

    public bool HasModuleSuggestion =>
        ModuleSuggestion is { ShouldSuggest: true, AutoLaunch: false };

    public string ModuleSuggestionMessage => ModuleSuggestion?.Message ?? string.Empty;

    public string ModuleSuggestionOpenLabel =>
        ModuleSuggestion?.ModuleId == AedaModuleId.Research.Value
            ? "Open in AEDA Research"
            : "Open in AEDA Code";

    public string? ActiveConversationModelOverride =>
        _activeConversation is null
            ? _draftConversationModelOverride
            : _conversationModelOverrides.TryGetValue(
                _activeConversation.Id,
                out var model)
                ? model
                : null;

    public bool HasConversationModelOverride =>
        !string.IsNullOrWhiteSpace(ActiveConversationModelOverride);

    public string ConversationModelOverrideLabel =>
        HasConversationModelOverride
            ? $"Model override: {ActiveConversationModelOverride}"
            : "Automatic routing";

    public bool IsSettingsOpen
    {
        get => ShellNavigation.IsSettingsVisible;
        set
        {
            if (value)
            {
                ShellNavigation.Navigate(new AedaShellRoute(AedaShellSection.Settings));
            }
            else if (ShellNavigation.IsSettingsVisible)
            {
                ShellNavigation.Navigate(new AedaShellRoute(AedaShellSection.Chat));
            }

            NotifyShellVisibilityChanged();
        }
    }

    public bool IsChatVisible => ShellNavigation.IsChatVisible;

    public bool IsDashboardVisible => ShellNavigation.IsDashboardVisible;

    public bool IsTaskCenterVisible => ShellNavigation.IsTaskCenterVisible;

    public bool IsCodeVisible => ShellNavigation.IsCodeVisible;

    public bool IsMemoryVisible => ShellNavigation.IsMemoryVisible;

    public bool IsResearchVisible => ShellNavigation.IsResearchVisible;

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
        await LoadConversationPreviewsAsync(conversations, cancellationToken);
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
        UpdateModuleSuggestion(value);
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    public void ApplySettings(ApplicationSettings settings)
    {
        var normalized = ApplicationSettingsValidator.Normalize(settings);
        SidebarColumnWidth = new GridLength(
            normalized.Appearance.CompactSidebar ? 240 : 280);
        SidebarPadding = new Thickness(
            normalized.Appearance.CompactSidebar ? 10 : 16);
        ShowConversationMetadata = !normalized.Appearance.CompactSidebar;
        ShowMessageMetadata = normalized.Appearance.ShowMessageMetadata;
        OnPropertyChanged(nameof(HasUnsupportedImageContext));
        OnPropertyChanged(nameof(CanSend));
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    public void OpenModule(AedaModuleDescriptor descriptor)
    {
        if (descriptor.Status == AedaModuleStatus.Unavailable)
        {
            StatusMessage = descriptor.SafeUnavailableReason ?? "Module unavailable.";
            return;
        }

        var section = descriptor.Kind switch
        {
            AedaModuleKind.Code => AedaShellSection.Code,
            AedaModuleKind.Memory => AedaShellSection.Memory,
            AedaModuleKind.Research => AedaShellSection.Research,
            AedaModuleKind.TaskCenter => AedaShellSection.TaskCenter,
            AedaModuleKind.Settings => AedaShellSection.Settings,
            _ => AedaShellSection.Dashboard
        };
        NavigateTo(
            section,
            descriptor.Id,
            descriptor.Route.RouteId);

        if (descriptor.Kind == AedaModuleKind.Code)
        {
            _ = AedaCode.InitializeAsync();
        }
        else if (descriptor.Kind == AedaModuleKind.Memory)
        {
            _ = AedaMemory.InitializeAsync();
        }
        else if (descriptor.Kind == AedaModuleKind.Research)
        {
            _ = AedaResearch.InitializeAsync();
        }
        else if (descriptor.Kind == AedaModuleKind.TaskCenter)
        {
            _ = TaskCenter.RefreshAsync();
        }
    }

    public bool OpenModuleById(AedaModuleId moduleId)
    {
        if (!_moduleRegistry.TryGetModule(moduleId, out var descriptor))
        {
            StatusMessage = "Module unavailable.";
            return false;
        }

        if (descriptor.Status == AedaModuleStatus.Unavailable)
        {
            StatusMessage = descriptor.SafeUnavailableReason ?? "Module unavailable.";
            return false;
        }

        OpenModule(descriptor);
        return true;
    }

    private void NavigateTo(
        AedaShellSection section,
        AedaModuleId? moduleId = null,
        string? routeId = null)
    {
        ShellNavigation.Navigate(new AedaShellRoute(section, moduleId, routeId));
        NotifyShellVisibilityChanged();
    }

    private void NotifyShellVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsSettingsOpen));
        OnPropertyChanged(nameof(IsChatVisible));
        OnPropertyChanged(nameof(IsDashboardVisible));
        OnPropertyChanged(nameof(IsTaskCenterVisible));
        OnPropertyChanged(nameof(IsCodeVisible));
        OnPropertyChanged(nameof(IsMemoryVisible));
        OnPropertyChanged(nameof(IsResearchVisible));
    }

    partial void OnModuleSuggestionChanged(ModuleSuggestion? value)
    {
        OnPropertyChanged(nameof(HasModuleSuggestion));
        OnPropertyChanged(nameof(ModuleSuggestionMessage));
        OnPropertyChanged(nameof(ModuleSuggestionOpenLabel));
        OpenSuggestedModuleCommand.NotifyCanExecuteChanged();
    }

    private void UpdateModuleSuggestion(string prompt)
    {
        var suggestion = _moduleSuggestionService.Suggest(prompt);
        ModuleSuggestion = suggestion.ShouldSuggest && !suggestion.AutoLaunch
            ? suggestion
            : null;
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
        RegenerateMessageCommand.NotifyCanExecuteChanged();
        RetryMessageCommand.NotifyCanExecuteChanged();
        AttachClipboardContextCommand.NotifyCanExecuteChanged();
        CaptureApplicationContextCommand.NotifyCanExecuteChanged();
        CaptureScreenshotContextCommand.NotifyCanExecuteChanged();
        RemoveContextCommand.NotifyCanExecuteChanged();
        ClearAllContextsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void OpenSettings()
    {
        NavigateTo(AedaShellSection.Settings);
    }

    [RelayCommand]
    public void CloseSettings()
    {
        NavigateTo(AedaShellSection.Chat);
    }

    [RelayCommand]
    public void OpenDashboard()
    {
        NavigateTo(AedaShellSection.Dashboard);
        _ = ModuleDashboard.RefreshTaskSummariesAsync();
        _ = TaskCenter.RefreshAsync();
    }

    [RelayCommand]
    public void OpenTaskCenter()
    {
        NavigateTo(AedaShellSection.TaskCenter);
        _ = TaskCenter.RefreshAsync();
    }

    [RelayCommand]
    public void OpenChat()
    {
        NavigateTo(AedaShellSection.Chat);
    }

    [RelayCommand(CanExecute = nameof(CanOpenSuggestedModule))]
    public void OpenSuggestedModule()
    {
        if (ModuleSuggestion is not { ShouldSuggest: true } suggestion)
        {
            return;
        }

        OpenModuleById(new AedaModuleId(suggestion.ModuleId));
        DismissModuleSuggestion();
    }

    [RelayCommand]
    public void DismissModuleSuggestion()
    {
        ModuleSuggestion = null;
    }

    private bool CanOpenSuggestedModule() =>
        ModuleSuggestion is { ShouldSuggest: true } suggestion &&
        _moduleRegistry.TryGetModule(
            new AedaModuleId(suggestion.ModuleId),
            out var descriptor) &&
        descriptor.Status != AedaModuleStatus.Unavailable;

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

    public async Task OpenConversationAsync(Guid conversationId)
    {
        OpenChat();
        await ReloadConversationListAsync(conversationId);
        await SelectConversationAsync(Conversations.FirstOrDefault(item =>
            item.Id == conversationId));
    }

    private void CreateNewDraftConversation()
    {
        _activeConversation = null;
        SelectedConversation = null;
        Messages.Clear();
        ClearAttachedContextItems();
        Prompt = string.Empty;
        _draftConversationModelOverride = null;
        CurrentConversationTitle = NewChatTitle;
        Status = ChatStatus.Ready;
        StatusMessage = "Ready";
        RoutingStatusMessage = "Automatic routing";
        NotifyConversationModelOverrideChanged();
        RefreshMessageActionEligibility();
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
        _draftConversationModelOverride = null;
        CurrentConversationTitle = storedConversation.Title;
        RoutingStatusMessage = HasConversationModelOverride
            ? $"Conversation override · {ActiveConversationModelOverride}"
            : "Automatic routing";
        Messages.Clear();
        var supersededToolCallIds = FindSupersededToolCallIds(messages);

        foreach (var message in messages)
        {
            if (IsSupersededToolCall(message, supersededToolCallIds))
            {
                continue;
            }

            Messages.Add(new ChatMessageViewModel(
                message.Role,
                FormatStoredMessage(message)));
        }

        SelectedConversation = Conversations.FirstOrDefault(
            item => item.Id == storedConversation.Id);
        Status = ChatStatus.Ready;
        StatusMessage = "Ready";
        RefreshMessageActionEligibility();
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

        var command = ExplicitModelOverrideParser.ParseCommand(prompt);
        if (command.Kind != ModelCommandKind.None)
        {
            await HandleModelCommandAsync(command);
            return;
        }

        Prompt = string.Empty;
        var contextSnapshot = _attachedContextCollection.Snapshot();
        await RunGenerationWithStateAsync(() => ExecuteGenerationAsync(
            prompt,
            contextSnapshot,
            persistUserMessage: true,
            clearAttachedContexts: true,
            priorHistory: null,
            completionStatus: ChatMessageDisplayStatus.Completed,
            existingAssistantMessage: null,
            explicitOneTurnModelOverride: null,
            cancellationToken: _generationCancellation!.Token));
    }

    [RelayCommand]
    public void UseAutomaticRouting()
    {
        ClearConversationModelOverride();
        StatusMessage = "Automatic routing enabled.";
        RoutingStatusMessage = "Automatic routing";
    }

    [RelayCommand]
    public void ChooseVisionModel()
    {
        IsSettingsOpen = true;
        StatusMessage = "Choose a vision-capable model in Settings.";
    }

    private async Task HandleModelCommandAsync(ModelCommandParseResult command)
    {
        if (command.Kind == ModelCommandKind.Malformed)
        {
            StatusMessage = command.ErrorMessage ?? "Model command was not understood.";
            RoutingStatusMessage = "Model command invalid";
            return;
        }

        if (command.Kind == ModelCommandKind.ClearConversationOverride)
        {
            Prompt = string.Empty;
            ClearConversationModelOverride();
            StatusMessage = "Automatic routing enabled.";
            RoutingStatusMessage = "Automatic routing";
            return;
        }

        if (command.Kind == ModelCommandKind.ConversationOverride)
        {
            if (!await TryValidateRequestedModelAsync(command.Model, CancellationToken.None))
            {
                return;
            }

            Prompt = string.Empty;
            SetConversationModelOverride(command.Model!);
            StatusMessage = $"Conversation model override set to {command.Model}.";
            RoutingStatusMessage = $"Conversation override · {command.Model}";
            return;
        }

        if (command.Kind == ModelCommandKind.OneTurnOverride)
        {
            if (!await TryValidateRequestedModelAsync(command.Model, CancellationToken.None))
            {
                return;
            }

            Prompt = string.Empty;
            var contextSnapshot = _attachedContextCollection.Snapshot();
            await RunGenerationWithStateAsync(() => ExecuteGenerationAsync(
                command.Prompt!,
                contextSnapshot,
                persistUserMessage: true,
                clearAttachedContexts: true,
                priorHistory: null,
                completionStatus: ChatMessageDisplayStatus.Completed,
                existingAssistantMessage: null,
                explicitOneTurnModelOverride: command.Model,
                cancellationToken: _generationCancellation!.Token));
        }
    }

    private async Task<bool> TryValidateRequestedModelAsync(
        string? model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            StatusMessage = "Specify a model name or auto.";
            RoutingStatusMessage = "Model command invalid";
            return false;
        }

        var installed = await GetInstalledModelsForRoutingAsync(cancellationToken);
        var resolved = installed.FirstOrDefault(candidate =>
            candidate.Equals(model.Trim(), StringComparison.OrdinalIgnoreCase));

        if (resolved is not null)
        {
            return true;
        }

        StatusMessage = $"Requested model '{model.Trim()}' is not installed.";
        RoutingStatusMessage = "Model unavailable";
        return false;
    }

    private async Task RunGenerationWithStateAsync(Func<Task> createGenerationTask)
    {
        if (_isSending)
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
        var generationTask = createGenerationTask();
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
            RegenerateMessageCommand.NotifyCanExecuteChanged();
            RetryMessageCommand.NotifyCanExecuteChanged();
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
        bool persistUserMessage,
        bool clearAttachedContexts,
        IReadOnlyList<ChatMessage>? priorHistory,
        ChatMessageDisplayStatus completionStatus,
        ChatMessageViewModel? existingAssistantMessage,
        string? explicitOneTurnModelOverride,
        CancellationToken cancellationToken)
    {
        ChatMessageViewModel? assistantMessage = existingAssistantMessage;
        var assistantResponse = new StringBuilder();
        var requestAccepted = false;
        var model = ModelRoutingSettings.DefaultModel;
        var toolActivityMessages = new Dictionary<string, ChatMessageViewModel>(
            StringComparer.Ordinal);
        TaskId? activeTaskId = null;

        try
        {
            var previousHistory = priorHistory ??
                Messages
                    .Select(message => new ChatMessage(message.Role, message.Content))
                    .ToArray();
            var routingDecision = await SelectModelAsync(
                prompt,
                contextSnapshot,
                explicitOneTurnModelOverride,
                cancellationToken);
            RoutingStatusMessage = routingDecision.UserVisibleReason;
            model = routingDecision.SelectedModel;
            assistantMessage?.SetModelMetadata(
                model,
                GetRoutingSourceLabel(routingDecision.Source));

            if (TryBlockUnsupportedImageRequest(
                    prompt,
                    contextSnapshot,
                    routingDecision,
                    persistUserMessage,
                    assistantMessage))
            {
                return;
            }

            var routedPrompt = routingDecision.RoutedPrompt.Trim();

            if (string.IsNullOrWhiteSpace(routedPrompt))
            {
                routedPrompt = prompt;
            }

            var conversation = await EnsureActiveConversationAsync(
                routedPrompt,
                model,
                CancellationToken.None);
            var turnTaskId = await _conversationSession.StartChatTaskAsync(
                conversation.Id,
                routedPrompt,
                model,
                CancellationToken.None);
            activeTaskId = turnTaskId;
            TaskTimeline.ObserveTask(turnTaskId);

            if (persistUserMessage)
            {
                await _conversationSession.AddMessageAsync(
                    conversation.Id,
                    ChatRole.User,
                    routedPrompt,
                    CancellationToken.None);

                _conversationPreviews[conversation.Id] =
                    ConversationTitleGenerator.CreatePreview(routedPrompt);
                AddMessage(new ChatMessageViewModel(ChatRole.User, routedPrompt));
            }

            Status = ChatStatus.Generating;
            StatusMessage = "Generating - Press Esc to stop";
            if (assistantMessage is not null)
            {
                assistantMessage.Content = string.Empty;
                assistantMessage.StartStreaming();
                RefreshMessageActionEligibility();
            }

            var requestMessages = AttachedContextPromptComposer.Compose(
                previousHistory,
                routedPrompt,
                contextSnapshot,
                _settingsService.Current.Context.MaxIndividualClipboardCharacters,
                _settingsService.Current.Context.MaxTotalTextContextCharacters);
            var canUseWorkspaceTools =
                _conversationSession.CanUseWorkspaceTools(model);

            if (canUseWorkspaceTools)
            {
                StatusMessage = _conversationSession.GetWorkspaceToolAvailabilityMessage(model);
            }
            else if (LooksLikeWorkspaceAccessRequest(routedPrompt))
            {
                StatusMessage = _conversationSession.GetWorkspaceToolAvailabilityMessage(model);
            }

            await foreach (var chunk in _conversationSession.StreamWithWorkspaceToolsAsync(
                               conversation.Id,
                               turnTaskId,
                               model,
                               requestMessages,
                               cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(chunk.ActivityMessage))
                {
                    AddOrUpdateToolActivity(
                        chunk,
                        toolActivityMessages);
                    StatusMessage = chunk.ActivityMessage;
                }

                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    if (!requestAccepted)
                    {
                        requestAccepted = true;
                        if (clearAttachedContexts)
                        {
                            ClearAttachedContextItemsAfterSend();
                        }
                    }

                    assistantResponse.Append(chunk.Content);
                    if (assistantMessage is null)
                    {
                        assistantMessage = new ChatMessageViewModel(
                            ChatRole.Assistant,
                            string.Empty,
                            status: ChatMessageDisplayStatus.Streaming,
                            modelName: model,
                            routingSourceLabel: GetRoutingSourceLabel(
                                routingDecision.Source))
                        {
                            IsStreaming = true
                        };
                        AddMessage(assistantMessage);
                    }

                    AppendAssistantText(assistantMessage, chunk.Content, conversation.Id);
                }
            }

            if (!requestAccepted)
            {
                if (clearAttachedContexts)
                {
                    ClearAttachedContextItemsAfterSend();
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

            assistantMessage?.CompleteRendering(completionStatus);
            RefreshMessageActionEligibility();

            _activeConversation = await _conversationSession.UpdateConversationAsync(
                conversation,
                ConversationStatus.Completed,
                model,
                CancellationToken.None);
            await _conversationSession.CompleteChatTaskAsync(
                turnTaskId,
                completedContent,
                CancellationToken.None);

            await ReloadConversationListAsync(_activeConversation.Id);
            Status = ChatStatus.Completed;
            StatusMessage = "Completed";
        }
        catch (OperationCanceledException)
        {
            if (assistantMessage is not null &&
                string.IsNullOrWhiteSpace(assistantMessage.Content))
            {
                assistantMessage.Content = "Cancelled";
            }

            assistantMessage ??= AddAssistantStatusMessage(
                "Cancelled",
                ChatMessageDisplayStatus.Cancelled);
            await PersistInterruptedGenerationAsync(
                assistantResponse.ToString(),
                ConversationStatus.Cancelled,
                model,
                assistantMessage,
                ChatMessageDisplayStatus.Cancelled);
            if (activeTaskId is { } taskId)
            {
                await _conversationSession.CancelChatTaskAsync(
                    taskId,
                    CancellationToken.None);
            }

            Status = ChatStatus.Cancelled;
            StatusMessage = "Cancelled";
        }
        catch (Exception)
        {
            if (assistantMessage is not null &&
                string.IsNullOrWhiteSpace(assistantMessage.Content))
            {
                assistantMessage.Content =
                    "The assistant response could not be completed.";
            }

            assistantMessage ??= AddAssistantStatusMessage(
                "The assistant response could not be completed.",
                ChatMessageDisplayStatus.Failed);
            await PersistInterruptedGenerationAsync(
                assistantResponse.ToString(),
                ConversationStatus.Error,
                model,
                assistantMessage,
                ChatMessageDisplayStatus.Failed);
            if (activeTaskId is { } taskId)
            {
                await _conversationSession.FailChatTaskAsync(
                    taskId,
                    "assistant_response_failed",
                    CancellationToken.None);
            }

            Status = ChatStatus.Failed;
            StatusMessage = "Failed: The assistant response could not be completed.";
        }
    }

    [RelayCommand(CanExecute = nameof(IsGenerating))]
    public void CancelGeneration()
    {
        RequestGenerationCancellation();
    }

    [RelayCommand]
    public async Task CopyMessageAsync(ChatMessageViewModel? message)
    {
        if (message is null)
        {
            return;
        }

        try
        {
            var text = message.IsAssistantMessage
                ? message.RenderedContent.PlainText
                : message.Content;
            await _clipboardWriter.CopyTextAsync(text);
            StatusMessage = "Copied";
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException ||
            exception is InvalidOperationException ||
            exception is System.Runtime.InteropServices.COMException)
        {
            StatusMessage = "Copy failed.";
        }
    }

    private bool CanRegenerateMessage(ChatMessageViewModel? message) =>
        message?.CanRegenerate == true && !IsGenerating && !_isSending;

    [RelayCommand(CanExecute = nameof(CanRegenerateMessage))]
    public async Task RegenerateMessageAsync(ChatMessageViewModel? message)
    {
        if (!CanRegenerateMessage(message) ||
            !TryGetGenerationPromptFor(message!, out var prompt, out var priorHistory))
        {
            return;
        }

        await RunGenerationWithStateAsync(() => ExecuteGenerationAsync(
            prompt,
            [],
            persistUserMessage: false,
            clearAttachedContexts: false,
            priorHistory: priorHistory,
            completionStatus: ChatMessageDisplayStatus.Regenerated,
            existingAssistantMessage: null,
            explicitOneTurnModelOverride: null,
            cancellationToken: _generationCancellation!.Token));
    }

    private bool CanRetryMessage(ChatMessageViewModel? message) =>
        message?.CanRetry == true && !IsGenerating && !_isSending;

    [RelayCommand(CanExecute = nameof(CanRetryMessage))]
    public async Task RetryMessageAsync(ChatMessageViewModel? message)
    {
        if (!CanRetryMessage(message) ||
            !TryGetGenerationPromptFor(message!, out var prompt, out var priorHistory))
        {
            return;
        }

        await RunGenerationWithStateAsync(() => ExecuteGenerationAsync(
            prompt,
            [],
            persistUserMessage: false,
            clearAttachedContexts: false,
            priorHistory: priorHistory,
            completionStatus: ChatMessageDisplayStatus.Completed,
            existingAssistantMessage: message,
            explicitOneTurnModelOverride: null,
            cancellationToken: _generationCancellation!.Token));
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
        if (!string.IsNullOrWhiteSpace(_draftConversationModelOverride))
        {
            _conversationModelOverrides[conversation.Id] =
                _draftConversationModelOverride;
            _draftConversationModelOverride = null;
            NotifyConversationModelOverrideChanged();
        }

        CurrentConversationTitle = conversation.Title;
        await ReloadConversationListAsync(conversation.Id);
        return conversation;
    }

    private async Task PersistInterruptedGenerationAsync(
        string assistantContent,
        ConversationStatus status,
        string model,
        ChatMessageViewModel? assistantMessage = null,
        ChatMessageDisplayStatus displayStatus = ChatMessageDisplayStatus.Completed)
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

        assistantMessage?.CompleteRendering(displayStatus);
        RefreshMessageActionEligibility();
        await ReloadConversationListAsync(_activeConversation.Id);
    }

    public bool TryAttachContext(AttachedContextItem item) =>
        AddAttachedContext(item, "Application context attached.");

    private bool AddAttachedContext(AttachedContextItem item, string successMessage)
    {
        if (AttachedContexts.Count >=
            _settingsService.Current.Context.MaxAttachedContextItems)
        {
            SetContextStatus(
                $"Remove an attached context before adding another. Limit: {_settingsService.Current.Context.MaxAttachedContextItems}.");
            return false;
        }

        if (!_attachedContextCollection.Add(item))
        {
            SetContextStatus("That context is already attached.");
            return false;
        }

        AttachedContexts.Add(item);
        NotifyAttachedContextsChanged();
        SetContextStatus(successMessage);
        return true;
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

    private void SetConversationModelOverride(string model)
    {
        var normalized = model.Trim();

        if (_activeConversation is null)
        {
            _draftConversationModelOverride = normalized;
        }
        else
        {
            _conversationModelOverrides[_activeConversation.Id] = normalized;
        }

        NotifyConversationModelOverrideChanged();
    }

    private void ClearConversationModelOverride()
    {
        if (_activeConversation is null)
        {
            _draftConversationModelOverride = null;
        }
        else
        {
            _conversationModelOverrides.Remove(_activeConversation.Id);
        }

        NotifyConversationModelOverrideChanged();
    }

    private void NotifyConversationModelOverrideChanged()
    {
        OnPropertyChanged(nameof(ActiveConversationModelOverride));
        OnPropertyChanged(nameof(HasConversationModelOverride));
        OnPropertyChanged(nameof(ConversationModelOverrideLabel));
        OnPropertyChanged(nameof(CanSend));
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    private void UpdateImageCapabilityStatus()
    {
        if (HasUnsupportedImageContext)
        {
            SetContextStatus(
                CreateVisionCapabilityMessage(
                    GetConfiguredVisionModel(),
                    fallbackReason: null));
        }
    }

    private bool TryBlockUnsupportedImageRequest(
        string prompt,
        IReadOnlyList<AttachedContextItem> contextSnapshot,
        ModelRoutingDecision routingDecision,
        bool restorePrompt,
        ChatMessageViewModel? assistantMessage)
    {
        if (!HasAttachedImageContext(contextSnapshot) ||
            (!routingDecision.IsCapabilityBlocked &&
                ChatModelCapabilityService.SupportsImages(
                    routingDecision.SelectedModel,
                    _settingsService.Current.Vision)))
        {
            return false;
        }

        var message = CreateVisionCapabilityMessage(
            routingDecision.SelectedModel,
            routingDecision.FallbackReason);
        RoutingStatusMessage = "Vision model required";
        Status = ChatStatus.Ready;
        StatusMessage = "Select a vision-capable model to retry.";
        ContextStatusMessage = message;

        if (restorePrompt && string.IsNullOrWhiteSpace(Prompt))
        {
            Prompt = prompt;
        }

        if (assistantMessage is null)
        {
            assistantMessage = AddAssistantStatusMessage(
                message,
                ChatMessageDisplayStatus.Blocked);
            assistantMessage.SetModelMetadata(
                routingDecision.SelectedModel,
                GetRoutingSourceLabel(routingDecision.Source));
        }
        else
        {
            assistantMessage.Content = message;
            assistantMessage.CompleteRendering(ChatMessageDisplayStatus.Blocked);
        }

        RefreshMessageActionEligibility();
        return true;
    }

    private bool ConfiguredVisionModelSupportsImages()
    {
        var visionModel = GetConfiguredVisionModel();
        return ChatModelCapabilityService.SupportsImages(
            visionModel,
            _settingsService.Current.Vision);
    }

    private string GetConfiguredVisionModel()
    {
        return _settingsService.Current.Models.Assignments
            .FirstOrDefault(assignment =>
                assignment.Category == ModelRoutingCategory.Vision)
            ?.Model ?? string.Empty;
    }

    private static bool HasAttachedImageContext(
        IEnumerable<AttachedContextItem> attachedContexts)
    {
        return attachedContexts.Any(context => context.Images.Count > 0);
    }

    private static string CreateVisionCapabilityMessage(
        string? model,
        string? fallbackReason)
    {
        if (!string.IsNullOrWhiteSpace(fallbackReason) &&
            fallbackReason.Contains(
                "No installed vision-capable model",
                StringComparison.OrdinalIgnoreCase))
        {
            return "No installed vision-capable model is available.";
        }

        var modelName = string.IsNullOrWhiteSpace(model)
            ? "The selected model"
            : model.Trim();

        return $"{modelName} cannot analyze images.";
    }

    private static string GetRoutingSourceLabel(ModelRoutingSource source) =>
        source switch
        {
            ModelRoutingSource.ExplicitOneTurnOverride => "Explicit model",
            ModelRoutingSource.ConversationOverride => "Conversation override",
            ModelRoutingSource.SettingsOverride => "Settings",
            ModelRoutingSource.SafeFallback => "Fallback",
            ModelRoutingSource.IncompatibleOverride => "Blocked model",
            _ => "Automatic"
        };

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
        await LoadConversationPreviewsAsync(conversations);
        ApplyConversationFilter(selectedConversationId ?? _activeConversation?.Id);
    }

    private async Task LoadConversationPreviewsAsync(
        IReadOnlyList<Conversation> conversations,
        CancellationToken cancellationToken = default)
    {
        foreach (var conversation in conversations)
        {
            if (_conversationPreviews.ContainsKey(conversation.Id))
            {
                continue;
            }

            var messages = await _conversationSession.LoadMessagesAsync(
                conversation.Id,
                cancellationToken);
            var firstUser = messages.FirstOrDefault(message => message.Role == ChatRole.User);
            _conversationPreviews[conversation.Id] = firstUser is null
                ? string.Empty
                : ConversationTitleGenerator.CreatePreview(firstUser.Content);
        }
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

    private ConversationListItem CreateListItem(Conversation conversation)
    {
        _conversationPreviews.TryGetValue(conversation.Id, out var preview);
        if (string.Equals(preview, conversation.Title, StringComparison.Ordinal))
        {
            preview = string.Empty;
        }

        return new ConversationListItem(
            conversation.Id,
            conversation.Title,
            preview ?? string.Empty);
    }

    private static string FormatStoredMessage(StoredChatMessage message)
    {
        if (message.Role != ChatRole.Tool)
        {
            return message.Content;
        }

        return ToolPresentationMapper.FormatStoredToolActivity(message.Content);
    }

    private static HashSet<string> FindSupersededToolCallIds(
        IReadOnlyList<StoredChatMessage> messages)
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
        return !string.IsNullOrWhiteSpace(callId) &&
            supersededToolCallIds.Contains(callId);
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
            if (root.TryGetProperty("toolCallId", out var id) &&
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
        RefreshMessageActionEligibility();
        NotifyMessageCollectionChanged();
    }

    private ChatMessageViewModel AddAssistantStatusMessage(
        string content,
        ChatMessageDisplayStatus status)
    {
        var message = new ChatMessageViewModel(
            ChatRole.Assistant,
            content,
            status: status);
        message.CompleteRendering(status);
        AddMessage(message);
        return message;
    }

    private bool TryGetGenerationPromptFor(
        ChatMessageViewModel assistantMessage,
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

    private void RefreshMessageActionEligibility()
    {
        ChatMessageViewModel? latestAssistant = null;

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
                message.Status == ChatMessageDisplayStatus.Completed);
        }

        RegenerateMessageCommand.NotifyCanExecuteChanged();
        RetryMessageCommand.NotifyCanExecuteChanged();
    }

    private void AddOrUpdateToolActivity(
        ChatChunk chunk,
        IDictionary<string, ChatMessageViewModel> toolActivityMessages)
    {
        if (string.IsNullOrWhiteSpace(chunk.ActivityMessage))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(chunk.ActivityKey) &&
            toolActivityMessages.TryGetValue(chunk.ActivityKey, out var existing))
        {
            existing.Content = chunk.ActivityMessage;
            return;
        }

        var message = new ChatMessageViewModel(
            ChatRole.Tool,
            chunk.ActivityMessage,
            chunk.ActivityKey);
        AddMessage(message);

        if (!string.IsNullOrWhiteSpace(chunk.ActivityKey))
        {
            toolActivityMessages[chunk.ActivityKey] = message;
        }
    }

    private async Task<ModelRoutingDecision> SelectModelAsync(
        string prompt,
        IReadOnlyList<AttachedContextItem> contextSnapshot,
        string? explicitOneTurnModelOverride,
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
            _settingsService.Current.Models.Assignments)
        {
            ExplicitModelOverride = explicitOneTurnModelOverride,
            ConversationModelOverride = ActiveConversationModelOverride
        };

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
