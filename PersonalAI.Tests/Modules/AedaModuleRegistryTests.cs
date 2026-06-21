using PersonalAI.Core.Modules;
using PersonalAI.Infrastructure.Modules;

namespace PersonalAI.Tests.Modules;

public sealed class AedaModuleRegistryTests
{
    [Fact]
    public void Registry_ListsModulesDeterministicallyAndFindsCapabilities()
    {
        var code = Module(
            AedaModuleId.Code,
            "AEDA Code",
            AedaModuleStatus.Available,
            sortOrder: 20,
            [new AedaModuleCapability("patch_proposal", "Patch proposal", AedaModuleCapabilityState.Available)]);
        var chat = Module(
            AedaModuleId.Chat,
            "Chat",
            AedaModuleStatus.Available,
            sortOrder: 10,
            []);
        var registry = new AedaModuleRegistry([code, chat]);

        var modules = registry.ListModules();
        var matches = registry.GetModulesByCapability("PATCH_PROPOSAL");

        Assert.Equal([AedaModuleId.Chat, AedaModuleId.Code], modules.Select(module => module.Id));
        Assert.True(registry.TryGetModule(AedaModuleId.Code, out var found));
        Assert.Equal("AEDA Code", found.DisplayName);
        Assert.Equal(AedaModuleStatus.Available, registry.GetAvailability(AedaModuleId.Code));
        Assert.Single(matches);
        Assert.Equal(AedaModuleId.Code, matches[0].Id);
    }

    [Fact]
    public void Registry_ExcludesUnavailableModulesFromEnabledList()
    {
        var registry = new AedaModuleRegistry([
            Module(AedaModuleId.Code, "AEDA Code", AedaModuleStatus.PartiallyAvailable, 20, []),
            Module(new AedaModuleId("claw"), "Claw", AedaModuleStatus.Unavailable, 30, [])
        ]);

        var enabled = registry.ListEnabledModules();

        Assert.Single(enabled);
        Assert.Equal(AedaModuleId.Code, enabled[0].Id);
        Assert.Equal(AedaModuleStatus.Unavailable, registry.GetAvailability(new AedaModuleId("missing")));
    }

    [Fact]
    public void Registry_RejectsDuplicateModuleIds()
    {
        Assert.Throws<InvalidOperationException>(() => new AedaModuleRegistry([
            Module(AedaModuleId.Code, "AEDA Code", AedaModuleStatus.Available, 20, []),
            Module(AedaModuleId.Code, "Code duplicate", AedaModuleStatus.Available, 30, [])
        ]));
    }

    private static AedaModuleDescriptor Module(
        AedaModuleId id,
        string displayName,
        AedaModuleStatus status,
        int sortOrder,
        IReadOnlyList<AedaModuleCapability> capabilities) =>
        new(
            id,
            id == AedaModuleId.Code ? AedaModuleKind.Code : AedaModuleKind.Chat,
            displayName,
            "Test module",
            "test",
            status,
            capabilities,
            new AedaModuleRoute(id.Value),
            status == AedaModuleStatus.Unavailable ? "module_unavailable" : null,
            sortOrder);
}
