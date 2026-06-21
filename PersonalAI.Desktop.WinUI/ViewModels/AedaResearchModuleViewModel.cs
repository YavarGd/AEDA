using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Research;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class AedaResearchModuleViewModel : ObservableObject
{
    private const int SummaryLimit = 8;
    private readonly IAedaResearchModuleService _moduleService;

    public AedaResearchModuleViewModel(
        IAedaResearchModuleService moduleService,
        IAedaModuleRegistry moduleRegistry)
    {
        _moduleService = moduleService ?? throw new ArgumentNullException(nameof(moduleService));
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        if (moduleRegistry.TryGetModule(AedaModuleId.Research, out var descriptor))
        {
            Descriptor = descriptor;
        }
        else
        {
            Descriptor = new AedaModuleDescriptor(
                AedaModuleId.Research,
                AedaModuleKind.Research,
                "AEDA Research",
                "AEDA Research is unavailable.",
                "\uE721",
                AedaModuleStatus.Unavailable,
                [],
                new AedaModuleRoute("aeda-research"),
                "aeda_research_module_unavailable",
                SortOrder: 40);
        }

        CapabilityBadges = Descriptor.Capabilities
            .Where(capability => capability.State == AedaModuleCapabilityState.Available)
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

    public string PrivacyStatusText =>
        Dashboard?.PrivacyStatus ?? "Local evidence only. Remote search is disabled by default.";

    public IReadOnlyList<EvidenceProviderStatus> ProviderStatuses =>
        Dashboard?.EvidenceProviders ?? [];

    public IReadOnlyList<VerificationReport> RecentReports =>
        Dashboard?.RecentReports ?? [];

    public IReadOnlyList<ResearchClaim> ExtractedClaims { get; private set; } = [];

    public IReadOnlyList<EvidenceItem> EvidenceItems =>
        SelectedReport?.Evidence.Take(SummaryLimit).ToArray() ?? [];

    public IReadOnlyList<VerificationFinding> Findings =>
        SelectedReport?.Findings ?? [];

    public IReadOnlyList<CitationReference> Citations =>
        SelectedReport?.Findings
            .SelectMany(finding => finding.Citations)
            .Take(SummaryLimit)
            .ToArray() ?? [];

    public IReadOnlyList<string> UnresolvedGaps =>
        SelectedReport?.UnresolvedGaps.Take(SummaryLimit).ToArray() ?? [];

    public bool CanExtractClaims => !string.IsNullOrWhiteSpace(VerificationText);

    public bool CanVerify => !string.IsNullOrWhiteSpace(VerificationText);

    public bool HasClaims => ExtractedClaims.Count > 0;

    public bool HasNoClaims => !HasClaims;

    public bool HasRecentReports => RecentReports.Count > 0;

    public bool HasNoRecentReports => !HasRecentReports;

    public bool HasSelectedReport => SelectedReport is not null;

    public bool HasEvidenceItems => EvidenceItems.Count > 0;

    public bool HasCitations => Citations.Count > 0;

    public bool HasUnresolvedGaps => UnresolvedGaps.Count > 0;

    [ObservableProperty]
    private AedaResearchDashboardModel? _dashboard;

    [ObservableProperty]
    private VerificationReport? _selectedReport;

    [ObservableProperty]
    private string _verificationText = string.Empty;

    [ObservableProperty]
    private string _safeStatusMessage = "Research module ready.";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Dashboard = await _moduleService.GetDashboardAsync(cancellationToken)
            .ConfigureAwait(false);
        SelectedReport = Dashboard.RecentReports.FirstOrDefault();
        SafeStatusMessage = Dashboard.SafeStatusMessage;
        NotifyDashboardChanged();
        NotifyReportChanged();
    }

    [RelayCommand(CanExecute = nameof(CanExtractClaims))]
    public async Task ExtractClaimsAsync()
    {
        ExtractedClaims = await _moduleService.ExtractClaimsAsync(
            VerificationText,
            SummaryLimit).ConfigureAwait(false);
        SafeStatusMessage = ExtractedClaims.Count == 0
            ? "No claims extracted."
            : "Claims extracted.";
        OnPropertyChanged(nameof(ExtractedClaims));
        OnPropertyChanged(nameof(HasClaims));
        OnPropertyChanged(nameof(HasNoClaims));
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
    public async Task VerifyWithLocalEvidenceAsync()
    {
        var report = await _moduleService.VerifyWithLocalEvidenceAsync(
            new VerificationRequest(VerificationText, new ResearchScope(MaxClaims: SummaryLimit)))
            .ConfigureAwait(false);
        SelectedReport = report;
        Dashboard = await _moduleService.GetDashboardAsync().ConfigureAwait(false);
        SafeStatusMessage = report.Status == VerificationReportStatus.Completed
            ? "Local verification report created."
            : "Verification did not complete.";
        NotifyDashboardChanged();
        NotifyReportChanged();
    }

    [RelayCommand]
    public void SelectReport(VerificationReport? report)
    {
        SelectedReport = report;
        SafeStatusMessage = report is null
            ? "No report selected."
            : "Research report selected.";
        NotifyReportChanged();
    }

    partial void OnDashboardChanged(AedaResearchDashboardModel? value)
    {
        NotifyDashboardChanged();
    }

    partial void OnSelectedReportChanged(VerificationReport? value)
    {
        NotifyReportChanged();
    }

    partial void OnVerificationTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanExtractClaims));
        OnPropertyChanged(nameof(CanVerify));
        ExtractClaimsCommand.NotifyCanExecuteChanged();
        VerifyWithLocalEvidenceCommand.NotifyCanExecuteChanged();
    }

    private void NotifyDashboardChanged()
    {
        OnPropertyChanged(nameof(PrivacyStatusText));
        OnPropertyChanged(nameof(ProviderStatuses));
        OnPropertyChanged(nameof(RecentReports));
        OnPropertyChanged(nameof(HasRecentReports));
        OnPropertyChanged(nameof(HasNoRecentReports));
    }

    private void NotifyReportChanged()
    {
        OnPropertyChanged(nameof(HasSelectedReport));
        OnPropertyChanged(nameof(EvidenceItems));
        OnPropertyChanged(nameof(Findings));
        OnPropertyChanged(nameof(Citations));
        OnPropertyChanged(nameof(UnresolvedGaps));
        OnPropertyChanged(nameof(HasEvidenceItems));
        OnPropertyChanged(nameof(HasCitations));
        OnPropertyChanged(nameof(HasUnresolvedGaps));
    }
}
