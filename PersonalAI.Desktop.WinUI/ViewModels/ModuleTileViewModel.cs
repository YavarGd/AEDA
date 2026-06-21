using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAI.Core.Modules;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class ModuleTileViewModel : ObservableObject
{
    private readonly Action<AedaModuleDescriptor>? _openModule;

    public ModuleTileViewModel(
        AedaModuleDescriptor descriptor,
        Action<AedaModuleDescriptor>? openModule = null)
    {
        Descriptor = descriptor;
        _openModule = openModule;
    }

    public AedaModuleDescriptor Descriptor { get; }

    public string DisplayName => Descriptor.DisplayName;

    public string ShortDescription => Descriptor.ShortDescription;

    public string Glyph => Descriptor.Glyph;

    public AedaModuleStatus Status => Descriptor.Status;

    public bool IsEnabled => Descriptor.Status != AedaModuleStatus.Unavailable;

    public string StatusLabel => Descriptor.Status switch
    {
        AedaModuleStatus.Available => "Available",
        AedaModuleStatus.PartiallyAvailable => "Needs setup",
        _ => "Coming later"
    };

    public string SafeUnavailableReason =>
        Descriptor.SafeUnavailableReason ?? "module_available";

    public bool HasUnavailableReason => !IsEnabled &&
        !string.IsNullOrWhiteSpace(Descriptor.SafeUnavailableReason);

    public AedaModuleKind Kind => Descriptor.Kind;

    public IReadOnlyList<string> CapabilityHints =>
        Descriptor.Capabilities
            .OrderBy(capability => capability.State)
            .ThenBy(capability => capability.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Select(capability => capability.DisplayName)
            .ToArray();

    public string CapabilityHintText => CapabilityHints.Count == 0
        ? "No shell capability advertised"
        : string.Join(" · ", CapabilityHints);

    public string AccessibleName =>
        $"{DisplayName}. {StatusLabel}. {ShortDescription}";

    public string ToolTipText => IsEnabled
        ? $"{DisplayName}: {ShortDescription}"
        : $"{DisplayName}: {SafeUnavailableReason}";

    [RelayCommand(CanExecute = nameof(IsEnabled))]
    private void Open() => _openModule?.Invoke(Descriptor);
}
