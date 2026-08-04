using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
    private bool _loadingProviders;
    private bool _loaded;

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

    public void FocusPrimaryAction() => ProviderPicker.Focus();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        viewModel.Workspaces.ConfirmRemoveWorkspaceAsync = ConfirmRemoveAsync;
        viewModel.Workspaces.RequestRenameWorkspaceAsync = RequestRenameAsync;
        LoadProviders();
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
        _themeManager?.AttachPlatformSettings(this.GetPlatformSettings());
        await viewModel.Workspaces.RefreshAsync();
        await viewModel.RefreshModelsAsync();
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
            await viewModel.RefreshModelsAsync();
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
}
