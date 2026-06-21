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

    public string? SafeUnavailableReason => Descriptor.SafeUnavailableReason;

    [RelayCommand(CanExecute = nameof(IsEnabled))]
    private void Open() => _openModule?.Invoke(Descriptor);
}
