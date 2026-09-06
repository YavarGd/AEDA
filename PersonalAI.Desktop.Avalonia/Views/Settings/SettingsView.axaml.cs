using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.ComponentModel;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.Avalonia.Themes;
using PersonalAI.Desktop.Avalonia.Views.Dialogs;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Desktop.Avalonia.Views.Settings;

public partial class SettingsView : UserControl
{
    private readonly Dictionary<string, string> _providerIds =
        new(StringComparer.Ordinal);
    private AvaloniaThemeManager? _themeManager;
    private IApplicationSettingsService? _settingsService;
    private SettingsViewModel? _statusViewModel;
    private bool _loadingProviders;
    private bool _loaded;
    private bool _suppressStatusBridge = true;
    private bool _suppressModelStatusBridge;
    private bool _compact;
    private bool _medium;
    private SettingsCategory _selectedCategory = SettingsCategory.Appearance;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public SettingsView(
        AvaloniaThemeManager themeManager,
        IApplicationSettingsService settingsService) : this()
    {
        _themeManager = themeManager;
        _settingsService = settingsService;
    }

    public void FocusPrimaryAction()
    {
        if (_compact)
        {
            SystemMicaThemeOption.Focus();
            return;
        }

        GetCategoryControl(_selectedCategory, _medium).Focus();
    }

    public void ApplyResponsiveMode(bool compact, bool medium)
    {
        _compact = compact;
        _medium = medium;
        Classes.Set("compact", compact);
        Classes.Set("medium", medium);

        var layout = ResolveLayout(compact, medium);
        PageLayout.Margin = new Thickness(layout.PagePadding);
        PageLayout.MaxWidth = layout.ContentMaxWidth;
        PageLayout.Spacing = layout.MajorGap;
        PageHeading.FontSize = layout.HeadingSize;
        MediumCategoryNavigation.IsVisible = medium;
        CategoryRail.IsVisible = !compact && !medium;
        SettingsWorkspace.ColumnDefinitions[0].Width =
            new GridLength(layout.RailWidth);
        SettingsWorkspace.ColumnSpacing = layout.RailWidth > 0
            ? layout.MajorGap
            : 0;
        CategoryContent.Spacing = compact ? layout.MajorGap : 0;
        UpdateCategoryPresentation();
    }

    internal static (
        double PagePadding,
        double ContentMaxWidth,
        double RailWidth,
        double MajorGap,
        double HeadingSize) ResolveLayout(bool compact, bool medium) =>
        compact
            ? (16, 640, 0, 16, 22)
            : medium
                ? (24, 900, 0, 20, 24)
                : (32, 1080, 224, 24, 28);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachStatusBridge();
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        viewModel.Workspaces.ConfirmRemoveWorkspaceAsync = ConfirmRemoveAsync;
        viewModel.Workspaces.RequestRenameWorkspaceAsync = RequestRenameAsync;
        AttachStatusBridge(viewModel);
        LoadProviders();
    }

    private void AttachStatusBridge(SettingsViewModel viewModel)
    {
        _statusViewModel = viewModel;
        viewModel.PropertyChanged += OnSettingsPropertyChanged;
        viewModel.Workspaces.PropertyChanged += OnWorkspacePropertyChanged;
    }

    private void DetachStatusBridge()
    {
        if (_statusViewModel is null)
        {
            return;
        }

        _statusViewModel.PropertyChanged -= OnSettingsPropertyChanged;
        _statusViewModel.Workspaces.PropertyChanged -= OnWorkspacePropertyChanged;
        _statusViewModel = null;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressStatusBridge ||
            _suppressModelStatusBridge ||
            e.PropertyName != nameof(SettingsViewModel.ModelRefreshStatus) ||
            sender is not SettingsViewModel viewModel)
        {
            return;
        }

        var status = SettingsPresentationConverters.SanitizeProviderStatus(
            viewModel.ModelRefreshStatus);
        if (!string.IsNullOrWhiteSpace(status))
        {
            viewModel.StatusMessage = status;
        }
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressStatusBridge ||
            e.PropertyName != nameof(WorkspaceManagementViewModel.StatusMessage) ||
            sender is not WorkspaceManagementViewModel workspaces ||
            _statusViewModel is null)
        {
            return;
        }

        var status = workspaces.StatusMessage;
        if (!string.IsNullOrWhiteSpace(status))
        {
            _statusViewModel.StatusMessage = status;
        }
    }

    private async void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        if (_loaded || DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        _loaded = true;
        _suppressStatusBridge = true;
        try
        {
            _themeManager?.AttachPlatformSettings(this.GetPlatformSettings());
            await viewModel.Workspaces.RefreshAsync();
            await viewModel.RefreshModelsAsync();
        }
        finally
        {
            _suppressStatusBridge = false;
        }
    }

    private void OnThemeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string value } &&
            Enum.TryParse<ThemePreference>(value, out var theme) &&
            DataContext is SettingsViewModel viewModel)
        {
            _themeManager?.Apply(theme);
            viewModel.Theme = theme;
        }
    }

    private void OnCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string value } &&
            Enum.TryParse<SettingsCategory>(value, out var category))
        {
            _selectedCategory = category;
            UpdateCategoryPresentation();
        }
    }

    private async void OnProviderSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_loadingProviders ||
            ProviderPicker.SelectedItem is not string label ||
            !_providerIds.TryGetValue(label, out var providerId) ||
            _settingsService is null)
        {
            return;
        }

        var current = _settingsService.Current;
        if (current.ProviderRouting.SelectedChatProvider == providerId)
        {
            return;
        }

        var routing = current.ProviderRouting with
        {
            SelectedChatProvider = providerId
        };
        await _settingsService.SaveAsync(current with { ProviderRouting = routing });
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.StatusMessage = "Chat provider updated.";
            _suppressModelStatusBridge = true;
            try
            {
                await viewModel.RefreshModelsAsync();
            }
            finally
            {
                _suppressModelStatusBridge = false;
            }
        }
    }

    private async void OnAddWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel ||
            GetOwner() is not { } owner)
        {
            return;
        }

        var confirmed = await WorkspaceDialog.ConfirmAsync(
            owner,
            "Add workspace",
            $"Add '{viewModel.Workspaces.PendingDisplayName}'? AEDA will register the folder without modifying its files.",
            "Add workspace");
        if (confirmed)
        {
            await viewModel.Workspaces.AddPendingWorkspaceAsync();
        }
    }

    private async void OnRenameWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WorkspaceItemViewModel workspace } &&
            DataContext is SettingsViewModel viewModel)
        {
            await viewModel.Workspaces.RenameWorkspaceAsync(workspace);
        }
    }

    private async void OnRevalidateWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WorkspaceItemViewModel workspace } &&
            DataContext is SettingsViewModel viewModel)
        {
            await viewModel.Workspaces.RevalidateWorkspaceAsync(workspace);
        }
    }

    private async void OnRemoveWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WorkspaceItemViewModel workspace } &&
            DataContext is SettingsViewModel viewModel)
        {
            await viewModel.Workspaces.RemoveWorkspaceAsync(workspace);
        }
    }

    private Task<bool> ConfirmRemoveAsync(WorkspaceItemViewModel workspace)
    {
        var owner = GetOwner();
        return owner is null
            ? Task.FromResult(false)
            : WorkspaceDialog.ConfirmAsync(
                owner,
                "Remove workspace",
                $"Remove '{workspace.DisplayName}' from AEDA? Files will not be deleted.",
                "Remove");
    }

    private Task<string?> RequestRenameAsync(WorkspaceItemViewModel workspace)
    {
        var owner = GetOwner();
        return owner is null
            ? Task.FromResult<string?>(null)
            : WorkspaceDialog.RenameAsync(owner, workspace.DisplayName);
    }

    private Window? GetOwner() => TopLevel.GetTopLevel(this) as Window;

    private void LoadProviders()
    {
        if (_settingsService is null)
        {
            return;
        }

        _loadingProviders = true;
        try
        {
            _providerIds.Clear();
            ProviderPicker.Items.Clear();
            var routing = _settingsService.Current.ProviderRouting;
            foreach (var provider in routing.ProviderProfiles.Where(item => item.IsEnabled))
            {
                var label = string.Equals(
                    provider.DisplayName,
                    provider.Id,
                    StringComparison.OrdinalIgnoreCase)
                    ? provider.DisplayName
                    : $"{provider.DisplayName} ({provider.Id})";
                _providerIds[label] = provider.Id;
                ProviderPicker.Items.Add(label);
                if (provider.Id == routing.SelectedChatProvider)
                {
                    ProviderPicker.SelectedItem = label;
                }
            }
        }
        finally
        {
            _loadingProviders = false;
        }
    }

    private void UpdateCategoryPresentation()
    {
        AppearancePanel.IsVisible = _compact || _selectedCategory == SettingsCategory.Appearance;
        AssistPanel.IsVisible = _compact || _selectedCategory == SettingsCategory.Assist;
        WindowPanel.IsVisible = _compact || _selectedCategory == SettingsCategory.Window;
        ProviderPanel.IsVisible = _compact || _selectedCategory == SettingsCategory.Provider;
        PrivacyPanel.IsVisible = _compact || _selectedCategory == SettingsCategory.Privacy;
        WorkspacesPanel.IsVisible = _compact || _selectedCategory == SettingsCategory.Workspaces;
        AdvancedPanel.IsVisible = _compact || _selectedCategory == SettingsCategory.Advanced;

        AppearanceCategory.IsChecked = AppearanceMediumCategory.IsChecked =
            _selectedCategory == SettingsCategory.Appearance;
        AssistCategory.IsChecked = AssistMediumCategory.IsChecked =
            _selectedCategory == SettingsCategory.Assist;
        WindowCategory.IsChecked = WindowMediumCategory.IsChecked =
            _selectedCategory == SettingsCategory.Window;
        ProviderCategory.IsChecked = ProviderMediumCategory.IsChecked =
            _selectedCategory == SettingsCategory.Provider;
        PrivacyCategory.IsChecked = PrivacyMediumCategory.IsChecked =
            _selectedCategory == SettingsCategory.Privacy;
        WorkspacesCategory.IsChecked = WorkspacesMediumCategory.IsChecked =
            _selectedCategory == SettingsCategory.Workspaces;
        AdvancedCategory.IsChecked = AdvancedMediumCategory.IsChecked =
            _selectedCategory == SettingsCategory.Advanced;
    }

    private RadioButton GetCategoryControl(SettingsCategory category, bool medium) =>
        (category, medium) switch
        {
            (SettingsCategory.Assist, false) => AssistCategory,
            (SettingsCategory.Window, false) => WindowCategory,
            (SettingsCategory.Provider, false) => ProviderCategory,
            (SettingsCategory.Privacy, false) => PrivacyCategory,
            (SettingsCategory.Workspaces, false) => WorkspacesCategory,
            (SettingsCategory.Advanced, false) => AdvancedCategory,
            (SettingsCategory.Assist, true) => AssistMediumCategory,
            (SettingsCategory.Window, true) => WindowMediumCategory,
            (SettingsCategory.Provider, true) => ProviderMediumCategory,
            (SettingsCategory.Privacy, true) => PrivacyMediumCategory,
            (SettingsCategory.Workspaces, true) => WorkspacesMediumCategory,
            (SettingsCategory.Advanced, true) => AdvancedMediumCategory,
            (_, true) => AppearanceMediumCategory,
            _ => AppearanceCategory
        };

    private enum SettingsCategory
    {
        Appearance,
        Assist,
        Window,
        Provider,
        Privacy,
        Workspaces,
        Advanced
    }
}
