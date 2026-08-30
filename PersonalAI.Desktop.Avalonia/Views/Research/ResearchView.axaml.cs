using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PersonalAI.Core.Research;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Desktop.Avalonia.Views.Research;

public partial class ResearchView : UserControl
{
    private bool _loaded;
    private bool _compact;
    private bool _medium;
    private CompactPane _compactPane = CompactPane.Overview;
    private Button? _lastOverviewButton;
    private Button? _lastReportButton;

    public ResearchView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public void FocusPrimaryAction()
    {
        if (_compact)
        {
            _compactPane = CompactPane.Claim;
            UpdatePresentation();
        }

        VerificationTextBox.Focus();
    }

    public void ApplyResponsiveMode(bool compact, bool medium)
    {
        var enteringCompact = compact && !_compact;
        _compact = compact;
        _medium = medium;
        if (enteringCompact)
        {
            _compactPane = CompactPane.Overview;
        }

        Classes.Set("compact", compact);
        Classes.Set("medium", medium);

        var layout = ResolveLayout(compact, medium);
        PageLayout.Margin = new Thickness(layout.PagePadding);
        PageLayout.RowSpacing = layout.MajorGap;
        PageHeading.FontSize = layout.HeadingSize;
        CapabilitySummary.Padding = new Thickness(layout.CapabilityPadding);
        WorkspaceLayout.ColumnSpacing = layout.MajorGap;
        WorkspaceLayout.RowSpacing = layout.MajorGap;
        ClaimColumn.Spacing = layout.MajorGap;

        ArrangeWorkspace(layout);
        UpdatePresentation();
    }

    internal static (
        double PagePadding,
        double CapabilityPadding,
        double ClaimWidth,
        double ReportsWidth,
        double MajorGap,
        double HeadingSize) ResolveLayout(bool compact, bool medium) =>
        compact
            ? (16, 16, 0, 0, 16, 22)
            : medium
                ? (24, 20, 0, 0, 20, 24)
                : (32, 24, 400, 320, 24, 28);

    private async void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        if (_loaded || DataContext is not AedaResearchModuleViewModel viewModel)
        {
            return;
        }

        _loaded = true;
        await viewModel.InitializeAsync();
    }

    private void OnOpenClaimPaneClick(object? sender, RoutedEventArgs e) =>
        OpenCompactPane(CompactPane.Claim, sender as Button, VerificationTextBox);

    private void OnOpenExtractedPaneClick(object? sender, RoutedEventArgs e) =>
        OpenCompactPane(CompactPane.Extracted, sender as Button, ExtractedClaimsSurface);

    private void OnOpenReportsPaneClick(object? sender, RoutedEventArgs e) =>
        OpenCompactPane(CompactPane.Reports, sender as Button, RecentReportsSurface);

    private void OnOpenSelectedReportPaneClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AedaResearchModuleViewModel { SelectedReport: not null })
        {
            return;
        }

        OpenCompactPane(CompactPane.Report, sender as Button, SelectedReportHeading);
    }

    private void OnReportClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: VerificationReport report } button ||
            DataContext is not AedaResearchModuleViewModel viewModel)
        {
            return;
        }

        viewModel.SelectReportCommand.Execute(report);
        if (!_compact || viewModel.SelectedReport?.Id != report.Id)
        {
            return;
        }

        _lastReportButton = button;
        _compactPane = CompactPane.Report;
        UpdatePresentation();
        FocusAfterLayout(SelectedReportHeading);
    }

    private void OnCompactBackClick(object? sender, RoutedEventArgs e)
    {
        if (!_compact)
        {
            return;
        }

        if (_compactPane == CompactPane.Report)
        {
            _compactPane = CompactPane.Reports;
            UpdatePresentation();
            if (_lastReportButton?.Focus() != true)
            {
                RecentReportsSurface.Focus();
            }

            return;
        }

        _compactPane = CompactPane.Overview;
        UpdatePresentation();
        if (_lastOverviewButton?.Focus() != true)
        {
            CompactOverview.Focus();
        }
    }

    private void OpenCompactPane(
        CompactPane pane,
        Button? origin,
        Control focusTarget)
    {
        if (!_compact)
        {
            return;
        }

        _lastOverviewButton = origin;
        _compactPane = pane;
        UpdatePresentation();
        FocusAfterLayout(focusTarget);
    }

    private void ArrangeWorkspace((
        double PagePadding,
        double CapabilityPadding,
        double ClaimWidth,
        double ReportsWidth,
        double MajorGap,
        double HeadingSize) layout)
    {
        if (_compact)
        {
            SetColumnWidths(GridLength.Star, Zero, Zero);
            SetRowHeights(GridLength.Star, Zero);
            Position(CompactOverview, 0, 0, 1);
            Position(ClaimColumn, 0, 0, 1);
            Position(RecentReportsSurface, 0, 0, 1);
            Position(SelectedReportSurface, 0, 0, 1);
            return;
        }

        if (_medium)
        {
            SetColumnWidths(GridLength.Star, GridLength.Star, Zero);
            SetRowHeights(GridLength.Auto, GridLength.Auto);
            Position(ClaimColumn, 0, 0, 1);
            Position(RecentReportsSurface, 0, 1, 1);
            Position(SelectedReportSurface, 1, 0, 2);
            Position(CompactOverview, 0, 0, 2);
            return;
        }

        SetColumnWidths(
            new GridLength(layout.ClaimWidth),
            new GridLength(layout.ReportsWidth),
            GridLength.Star);
        SetRowHeights(GridLength.Star, Zero);
        Position(ClaimColumn, 0, 0, 1);
        Position(RecentReportsSurface, 0, 1, 1);
        Position(SelectedReportSurface, 0, 2, 1);
        Position(CompactOverview, 0, 0, 3);
    }

    private void UpdatePresentation()
    {
        var overview = _compact && _compactPane == CompactPane.Overview;
        var claim = _compact && _compactPane == CompactPane.Claim;
        var extracted = _compact && _compactPane == CompactPane.Extracted;
        var reports = _compact && _compactPane == CompactPane.Reports;
        var report = _compact && _compactPane == CompactPane.Report;

        PageHeader.IsVisible = !_compact || overview;
        CompactBackButton.IsVisible = _compact && !overview;
        CompactBackButton.Content = report
            ? "Back to Recent reports"
            : "Back to Research overview";
        AutomationProperties.SetName(
            CompactBackButton,
            report ? "Back to Recent reports" : "Back to Research overview");

        CompactOverview.IsVisible = overview;
        ClaimColumn.IsVisible = !_compact || claim || extracted;
        ClaimWorkspaceSurface.IsVisible = !_compact || claim;
        ExtractedClaimsSurface.IsVisible = !_compact || extracted;
        RecentReportsSurface.IsVisible = !_compact || reports;
        SelectedReportSurface.IsVisible = !_compact || report;
    }

    private static void FocusAfterLayout(Control control) =>
        Dispatcher.UIThread.Post(
            () => control.Focus(),
            DispatcherPriority.Input);

    private void SetColumnWidths(
        GridLength first,
        GridLength second,
        GridLength third)
    {
        WorkspaceLayout.ColumnDefinitions[0].Width = first;
        WorkspaceLayout.ColumnDefinitions[1].Width = second;
        WorkspaceLayout.ColumnDefinitions[2].Width = third;
    }

    private void SetRowHeights(GridLength first, GridLength second)
    {
        WorkspaceLayout.RowDefinitions[0].Height = first;
        WorkspaceLayout.RowDefinitions[1].Height = second;
    }

    private static void Position(
        Control control,
        int row,
        int column,
        int columnSpan)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetColumnSpan(control, columnSpan);
    }

    private static GridLength Zero => new(0);

    private enum CompactPane
    {
        Overview,
        Claim,
        Extracted,
        Reports,
        Report
    }
}
