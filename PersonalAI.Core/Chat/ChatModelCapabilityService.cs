namespace PersonalAI.Core.Chat;

using PersonalAI.Core.Settings;

public static class ChatModelCapabilityService
{
    public static bool SupportsImages(string model)
    {
        return SupportsImages(model, settings: null);
    }

    public static bool SupportsImages(string model, VisionSettings? settings)
    {
        return VisionModelCapabilityRegistry.SupportsImages(model, settings);
    }

    public static bool SupportsTools(string model)
    {
        return ToolModelCapabilityRegistry.SupportsTools(model);
    }
}
