using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PersonalAI.Core.Modules;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class ModuleRegistryViewModel : ObservableObject
{
    public ModuleRegistryViewModel(
        IAedaModuleRegistry registry,
        Action<AedaModuleDescriptor>? openModule = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        Modules = new ObservableCollection<ModuleTileViewModel>(
            registry.ListModules()
                .Select(module => new ModuleTileViewModel(module, openModule)));
    }

    public ObservableCollection<ModuleTileViewModel> Modules { get; }
}
