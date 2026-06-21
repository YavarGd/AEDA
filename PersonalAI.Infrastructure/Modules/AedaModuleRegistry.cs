using PersonalAI.Core.Modules;

namespace PersonalAI.Infrastructure.Modules;

public sealed class AedaModuleRegistry : IAedaModuleRegistry
{
    private readonly IReadOnlyDictionary<AedaModuleId, AedaModuleDescriptor> _modules;

    public AedaModuleRegistry(IEnumerable<AedaModuleDescriptor> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        _modules = modules
            .GroupBy(module => module.Id)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    if (group.Count() > 1)
                    {
                        throw new InvalidOperationException("duplicate_module_id");
                    }

                    return group.Single();
                });
    }

    public IReadOnlyList<AedaModuleDescriptor> ListModules() =>
        _modules.Values
            .OrderBy(module => module.SortOrder)
            .ThenBy(module => module.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<AedaModuleDescriptor> ListEnabledModules() =>
        ListModules()
            .Where(module => module.Status != AedaModuleStatus.Unavailable)
            .ToArray();

    public bool TryGetModule(
        AedaModuleId moduleId,
        out AedaModuleDescriptor module) =>
        _modules.TryGetValue(moduleId, out module!);

    public IReadOnlyList<AedaModuleDescriptor> GetModulesByCapability(
        string capabilityId) =>
        ListModules()
            .Where(module => module.Capabilities.Any(capability =>
                capability.Id.Equals(capabilityId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

    public AedaModuleStatus GetAvailability(AedaModuleId moduleId) =>
        _modules.TryGetValue(moduleId, out var module)
            ? module.Status
            : AedaModuleStatus.Unavailable;
}
