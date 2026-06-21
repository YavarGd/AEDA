using CommunityToolkit.Mvvm.ComponentModel;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class AedaCodeModuleViewModel : ObservableObject
{
    private const int SummaryLimit = 6;
    private readonly IAedaCodeModuleService _moduleService;

    public AedaCodeModuleViewModel(
        IAedaCodeModuleService moduleService,
        IAedaModuleRegistry moduleRegistry)
    {
        _moduleService = moduleService ??
            throw new ArgumentNullException(nameof(moduleService));
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        if (moduleRegistry.TryGetModule(AedaModuleId.Code, out var descriptor))
        {
            Descriptor = descriptor;
            CapabilityBadges = descriptor.Capabilities
                .Where(capability =>
                    capability.State != AedaModuleCapabilityState.Deferred)
                .Take(6)
                .Select(capability => capability.DisplayName)
                .ToArray();
        }
        else
        {
            Descriptor = new AedaModuleDescriptor(
                AedaModuleId.Code,
                AedaModuleKind.Code,
                "AEDA Code",
                "AEDA Code is unavailable.",
                "\uE943",
                AedaModuleStatus.Unavailable,
                [],
                new AedaModuleRoute("aeda-code"),
                "aeda_code_module_unavailable",
                SortOrder: 20);
            CapabilityBadges = [];
        }
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

    public string WorkspaceSummary => Dashboard?.Workspace is null
        ? "No AEDA Code workspace session is active."
        : $"{Dashboard.Workspace.DisplayName} · {(Dashboard.Workspace.IsReadOnly ? "read-only" : "write approval required")} · {Dashboard.Workspace.ImmediateFileCount?.ToString() ?? "unknown"} files";

    public IReadOnlyList<AedaCodeSession> RecentSessions { get; private set; } = [];

    public IReadOnlyList<AedaCodeProposalSummary> ProposalSummaries =>
        Dashboard?.Proposals.Take(SummaryLimit).ToArray() ?? [];

    public IReadOnlyList<AedaCodeApplySummary> ApplySummaries =>
        Dashboard?.ApplyResults.Take(SummaryLimit).ToArray() ?? [];

    public IReadOnlyList<AedaCodeValidationSummary> ValidationSummaries =>
        Dashboard?.ValidationRuns.Take(SummaryLimit).ToArray() ?? [];

    public IReadOnlyList<AedaCodeTimelineItem> TimelineSummaries =>
        Dashboard?.Timeline.Take(SummaryLimit).ToArray() ?? [];

    public bool HasRecentSessions => RecentSessions.Count > 0;

    public bool HasNoRecentSessions => !HasRecentSessions;

    public bool HasDashboard => Dashboard is not null;

    public bool HasProposalSummaries => ProposalSummaries.Count > 0;

    public bool HasNoProposalSummaries => !HasProposalSummaries;

    public bool HasValidationSummaries => ValidationSummaries.Count > 0;

    public bool HasNoValidationSummaries => !HasValidationSummaries;

    public bool HasApplySummaries => ApplySummaries.Count > 0;

    public bool HasNoApplySummaries => !HasApplySummaries;

    public bool HasTimelineSummaries => TimelineSummaries.Count > 0;

    public bool HasNoTimelineSummaries => !HasTimelineSummaries;

    [ObservableProperty]
    private AedaCodeSession? _session;

    [ObservableProperty]
    private AedaCodeDashboardModel? _dashboard;

    [ObservableProperty]
    private string? _safeStatusMessage;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        RecentSessions = await _moduleService.ListRecentSessionsAsync(
            SummaryLimit,
            cancellationToken).ConfigureAwait(false);
        SafeStatusMessage = RecentSessions.Count == 0
            ? "No recent AEDA Code sessions."
            : "Recent AEDA Code sessions loaded.";
        OnPropertyChanged(nameof(RecentSessions));
        OnPropertyChanged(nameof(HasRecentSessions));
        OnPropertyChanged(nameof(HasNoRecentSessions));
    }

    public async Task StartSessionAsync(
        WorkspaceId workspaceId,
        string? safeSummary = null,
        CancellationToken cancellationToken = default)
    {
        Session = await _moduleService.StartSessionAsync(
            workspaceId,
            safeSummary,
            cancellationToken).ConfigureAwait(false);
        Dashboard = await _moduleService.GetDashboardAsync(
            Session.Id,
            cancellationToken).ConfigureAwait(false);
        SafeStatusMessage = "AEDA Code session ready";
        NotifyDashboardChanged();
    }

    public async Task RefreshDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        if (Session is null)
        {
            SafeStatusMessage = "No AEDA Code session";
            return;
        }

        Dashboard = await _moduleService.GetDashboardAsync(
            Session.Id,
            cancellationToken).ConfigureAwait(false);
        SafeStatusMessage = "AEDA Code dashboard refreshed";
        NotifyDashboardChanged();
    }

    partial void OnDashboardChanged(AedaCodeDashboardModel? value)
    {
        NotifyDashboardChanged();
    }

    private void NotifyDashboardChanged()
    {
        OnPropertyChanged(nameof(WorkspaceSummary));
        OnPropertyChanged(nameof(ProposalSummaries));
        OnPropertyChanged(nameof(ApplySummaries));
        OnPropertyChanged(nameof(ValidationSummaries));
        OnPropertyChanged(nameof(TimelineSummaries));
        OnPropertyChanged(nameof(HasDashboard));
        OnPropertyChanged(nameof(HasProposalSummaries));
        OnPropertyChanged(nameof(HasNoProposalSummaries));
        OnPropertyChanged(nameof(HasValidationSummaries));
        OnPropertyChanged(nameof(HasNoValidationSummaries));
        OnPropertyChanged(nameof(HasApplySummaries));
        OnPropertyChanged(nameof(HasNoApplySummaries));
        OnPropertyChanged(nameof(HasTimelineSummaries));
        OnPropertyChanged(nameof(HasNoTimelineSummaries));
    }
}
