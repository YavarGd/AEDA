using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Modules;

namespace PersonalAI.Desktop.Presentation.ViewModels;

public sealed partial class AedaMemoryModuleViewModel : ObservableObject
{
    private const int SummaryLimit = 8;
    private const string StorageFailureMessage =
        "Memory records could not be loaded or saved.";
    private readonly IAedaMemoryModuleService _moduleService;
    private long _newMemoryDraftVersion;

    public AedaMemoryModuleViewModel(
        IAedaMemoryModuleService moduleService,
        IAedaModuleRegistry moduleRegistry)
    {
        _moduleService = moduleService ??
            throw new ArgumentNullException(nameof(moduleService));
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        if (moduleRegistry.TryGetModule(AedaModuleId.Memory, out var descriptor))
        {
            Descriptor = descriptor;
        }
        else
        {
            Descriptor = new AedaModuleDescriptor(
                AedaModuleId.Memory,
                AedaModuleKind.Memory,
                "AEDA Memory",
                "AEDA Memory is unavailable.",
                "\uE8F1",
                AedaModuleStatus.Unavailable,
                [],
                new AedaModuleRoute("aeda-memory"),
                "aeda_memory_module_unavailable",
                SortOrder: 30);
        }

        CapabilityBadges = Descriptor.Capabilities
            .Where(capability => capability.State != AedaModuleCapabilityState.Deferred)
            .Take(8)
            .Select(capability => capability.DisplayName)
            .ToArray();
    }

    public AedaModuleDescriptor Descriptor { get; }

    public IReadOnlyList<string> CapabilityBadges { get; }

    public string DisplayName => Descriptor.DisplayName;

    public string ShortDescription => Descriptor.ShortDescription;

    public string AvailabilityLabel => Descriptor.Status switch
    {
        AedaModuleStatus.Available => "Available",
        AedaModuleStatus.PartiallyAvailable => "Needs setup",
        _ => "Unavailable"
    };

    public bool CanCreateMemory =>
        HasCapability(BackendCapability.MemoryEdit) &&
        !string.IsNullOrWhiteSpace(NewMemoryText) &&
        !string.IsNullOrWhiteSpace(NewMemorySourceReason);

    public bool CanSearchMemories =>
        HasCapability(BackendCapability.MemorySearch) &&
        !string.IsNullOrWhiteSpace(SearchText);

    public bool CanPreviewRetrieval =>
        HasCapability(BackendCapability.RetrievalPreview) &&
        !string.IsNullOrWhiteSpace(RetrievalQuery);

    public bool HasDashboard => Dashboard is not null;

    public bool HasRecentMemories => Dashboard?.RecentMemories.Count > 0;

    public bool HasNoRecentMemories => !HasRecentMemories;

    public bool HasTaskOutcomes => Dashboard?.RecentTaskOutcomes.Count > 0;

    public bool HasNoTaskOutcomes => !HasTaskOutcomes;

    public bool HasDocuments => Dashboard?.RecentDocuments.Count > 0;

    public bool HasNoDocuments => !HasDocuments;

    public bool HasStoredContent => HasRecentMemories || HasTaskOutcomes || HasDocuments;

    public bool HasNoStoredContent => !HasStoredContent;

    public bool HasSelectedMemory => SelectedMemory is not null;

    public bool HasNoSelectedMemory => !HasSelectedMemory;

    public bool HasSearchResults => SearchResults.Count > 0;

    public bool HasRetrievalPreview => RetrievalPreview.Count > 0;

    public string TotalMemoryCountText =>
        Dashboard is null ? "0 memories" : $"{Dashboard.TotalMemoryCount} memories";

    public string IndexedKnowledgeText =>
        Dashboard is null
            ? "0 documents, 0 chunks"
            : $"{Dashboard.IndexedDocumentCount} documents, {Dashboard.IndexedChunkCount} chunks";

    public string PrivacyStatusText =>
        Dashboard is null
            ? "Local-first memory."
            : $"{Dashboard.Privacy.LocalOnlyStatus}. {Dashboard.Privacy.AutomaticMemoryStatus}.";

    public string RetrievalStatusText =>
        Dashboard is null
            ? "Retrieval not loaded."
            : Dashboard.RetrievalAvailable
                ? "Retrieval preview available."
                : "Retrieval preview unavailable.";

    public IReadOnlyList<string> CountSummaries =>
        Dashboard is null
            ? []
            : Dashboard.CountsByKind
                .Concat(Dashboard.CountsByScope)
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => $"{item.Key}: {item.Value}")
                .ToArray();

    public IReadOnlyList<AedaMemoryRecordSummary> SearchResults { get; private set; } = [];

    public IReadOnlyList<AedaRetrievalPreviewItem> RetrievalPreview { get; private set; } = [];

    [ObservableProperty]
    private AedaMemoryDashboardModel? _dashboard;

    [ObservableProperty]
    private AedaMemoryRecordDetail? _selectedMemory;

    [ObservableProperty]
    private string _safeStatusMessage = "Memory module ready.";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _retrievalQuery = string.Empty;

    [ObservableProperty]
    private string _newMemoryText = string.Empty;

    [ObservableProperty]
    private string _newMemorySourceReason = "Explicit user save";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Dashboard = await _moduleService.GetDashboardAsync(cancellationToken);
        SafeStatusMessage = Dashboard.SafeStatusMessage;
        NotifyDashboardChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSearchMemories))]
    public async Task SearchMemoriesAsync()
    {
        SearchResults = await _moduleService.SearchMemoriesAsync(
            SearchText,
            SummaryLimit);
        SafeStatusMessage = SearchResults.Count == 0
            ? "No memories matched."
            : "Memory search complete.";
        OnPropertyChanged(nameof(SearchResults));
        OnPropertyChanged(nameof(HasSearchResults));
    }

    [RelayCommand(CanExecute = nameof(CanCreateMemory))]
    public async Task CreateExplicitMemoryAsync()
    {
        var draftVersion = _newMemoryDraftVersion;
        var text = NewMemoryText;
        var sourceReason = NewMemorySourceReason;
        var result = await RunMemoryOperationAsync(
            () => _moduleService.CreateExplicitMemoryAsync(
                new AedaMemoryCreateRequest(
                    MemoryKind.ExplicitUserPreference,
                    MemoryScope.Global,
                    text,
                    sourceReason)),
            "Memory save cancelled.",
            "Memory could not be saved. Try again.");

        if (result is null)
        {
            return;
        }

        if (!result.Succeeded)
        {
            SafeStatusMessage = result.SafeReasonCode ?? "Memory was not saved.";
            return;
        }

        if (draftVersion == _newMemoryDraftVersion)
        {
            NewMemoryText = string.Empty;
        }

        SafeStatusMessage = "Explicit memory saved.";
        await RefreshAfterMutationAsync(
            "Memory was saved, but the dashboard could not be refreshed.");
    }

    [RelayCommand]
    public async Task OpenMemoryDetailAsync(AedaMemoryRecordSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        SelectedMemory = await _moduleService.GetMemoryDetailAsync(
            new MemoryId(summary.Id));
        SafeStatusMessage = SelectedMemory is null
            ? "Memory not found."
            : "Memory detail loaded.";
    }

    [RelayCommand]
    public async Task ArchiveMemoryAsync(AedaMemoryRecordSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        var result = await RunMemoryOperationAsync(
            () => _moduleService.ArchiveMemoryAsync(new MemoryId(summary.Id)),
            "Memory archive cancelled.",
            "Memory could not be archived. Try again.");
        if (result is null)
        {
            return;
        }

        if (!result.Succeeded)
        {
            SafeStatusMessage = result.SafeReasonCode ?? "Memory was not archived.";
            return;
        }

        SafeStatusMessage = "Memory archived.";
        await RefreshAfterMutationAsync(
            "Memory was archived, but the dashboard could not be refreshed.");
    }

    [RelayCommand]
    public async Task DeleteMemoryAsync(AedaMemoryRecordSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        var result = await _moduleService.DeleteMemoryAsync(
            new MemoryId(summary.Id));
        SafeStatusMessage = result.Succeeded
            ? "Memory deleted."
            : result.SafeReasonCode ?? "Memory was not deleted.";
        await InitializeAsync();
    }

    [RelayCommand(CanExecute = nameof(CanPreviewRetrieval))]
    public async Task PreviewRetrievalAsync()
    {
        RetrievalPreview = await _moduleService.PreviewRetrievalAsync(
            RetrievalQuery,
            SummaryLimit);
        SafeStatusMessage = RetrievalPreview.Count == 0
            ? "No retrieval preview items."
            : "Retrieval preview loaded.";
        OnPropertyChanged(nameof(RetrievalPreview));
        OnPropertyChanged(nameof(HasRetrievalPreview));
    }

    partial void OnDashboardChanged(AedaMemoryDashboardModel? value)
    {
        NotifyDashboardChanged();
    }

    partial void OnSelectedMemoryChanged(AedaMemoryRecordDetail? value)
    {
        OnPropertyChanged(nameof(HasSelectedMemory));
        OnPropertyChanged(nameof(HasNoSelectedMemory));
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanSearchMemories));
        SearchMemoriesCommand.NotifyCanExecuteChanged();
    }

    partial void OnRetrievalQueryChanged(string value)
    {
        OnPropertyChanged(nameof(CanPreviewRetrieval));
        PreviewRetrievalCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewMemoryTextChanged(string value)
    {
        _newMemoryDraftVersion++;
        OnPropertyChanged(nameof(CanCreateMemory));
        CreateExplicitMemoryCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewMemorySourceReasonChanged(string value)
    {
        _newMemoryDraftVersion++;
        OnPropertyChanged(nameof(CanCreateMemory));
        CreateExplicitMemoryCommand.NotifyCanExecuteChanged();
    }

    private async Task<AedaMemoryOperationResult?> RunMemoryOperationAsync(
        Func<Task<AedaMemoryOperationResult>> operation,
        string cancellationStatus,
        string failureStatus)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            SafeStatusMessage = cancellationStatus;
        }
        catch (InvalidOperationException exception) when (
            string.Equals(exception.Message, StorageFailureMessage, StringComparison.Ordinal))
        {
            SafeStatusMessage = failureStatus;
        }

        return null;
    }

    private async Task RefreshAfterMutationAsync(string failureStatus)
    {
        try
        {
            await InitializeAsync();
        }
        catch (OperationCanceledException)
        {
            SafeStatusMessage = "Memory refresh cancelled.";
        }
        catch (InvalidOperationException exception) when (
            string.Equals(exception.Message, StorageFailureMessage, StringComparison.Ordinal))
        {
            SafeStatusMessage = failureStatus;
        }
    }

    private void NotifyDashboardChanged()
    {
        OnPropertyChanged(nameof(HasDashboard));
        OnPropertyChanged(nameof(HasRecentMemories));
        OnPropertyChanged(nameof(HasNoRecentMemories));
        OnPropertyChanged(nameof(HasTaskOutcomes));
        OnPropertyChanged(nameof(HasNoTaskOutcomes));
        OnPropertyChanged(nameof(HasDocuments));
        OnPropertyChanged(nameof(HasNoDocuments));
        OnPropertyChanged(nameof(HasStoredContent));
        OnPropertyChanged(nameof(HasNoStoredContent));
        OnPropertyChanged(nameof(TotalMemoryCountText));
        OnPropertyChanged(nameof(IndexedKnowledgeText));
        OnPropertyChanged(nameof(PrivacyStatusText));
        OnPropertyChanged(nameof(RetrievalStatusText));
        OnPropertyChanged(nameof(CountSummaries));
        OnPropertyChanged(nameof(CanCreateMemory));
        OnPropertyChanged(nameof(CanSearchMemories));
        OnPropertyChanged(nameof(CanPreviewRetrieval));
        CreateExplicitMemoryCommand.NotifyCanExecuteChanged();
        SearchMemoriesCommand.NotifyCanExecuteChanged();
        PreviewRetrievalCommand.NotifyCanExecuteChanged();
    }

    private bool HasCapability(BackendCapability capability) =>
        Descriptor.Capabilities.Any(item =>
            item.BackendCapability == capability &&
            item.State == AedaModuleCapabilityState.Available);
}
