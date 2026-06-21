using PersonalAI.Core.Modules;

namespace PersonalAI.Infrastructure.Modules;

public static class AedaDeferredModuleDescriptorFactory
{
    public static IReadOnlyList<AedaModuleDescriptor> CreateAll() =>
    [
        Deferred(
            new AedaModuleId("claw"),
            AedaModuleKind.Claw,
            "AEDA Claw",
            "High-agency workflows are deferred behind safety design.",
            "\uE7C1",
            "claw_module_deferred",
            50),
        Deferred(
            new AedaModuleId("pic-studio"),
            AedaModuleKind.PicStudio,
            "AEDA Pic Studio",
            "Image workflows are deferred for a later milestone.",
            "\uE91B",
            "pic_studio_module_deferred",
            60),
        Deferred(
            new AedaModuleId("office"),
            AedaModuleKind.Office,
            "AEDA Office",
            "Document and spreadsheet workflows are deferred.",
            "\uE8A5",
            "office_module_deferred",
            70),
        Deferred(
            new AedaModuleId("voice"),
            AedaModuleKind.Voice,
            "Voice",
            "Voice UI is deferred; no always-on audio is enabled.",
            "\uE720",
            "voice_module_deferred",
            80)
    ];

    private static AedaModuleDescriptor Deferred(
        AedaModuleId id,
        AedaModuleKind kind,
        string displayName,
        string description,
        string glyph,
        string reason,
        int sortOrder) =>
        new(
            id,
            kind,
            displayName,
            description,
            glyph,
            AedaModuleStatus.Unavailable,
            [
                new AedaModuleCapability(
                    $"{id.Value}_shell_entry",
                    "Shell entry planned",
                    AedaModuleCapabilityState.Deferred,
                    reason)
            ],
            new AedaModuleRoute(id.Value),
            reason,
            sortOrder);
}
