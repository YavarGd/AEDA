namespace PersonalAI.Core.Chat;

public static class ChatModelCapabilityService
{
    private static readonly string[] VisionModelNameFragments =
    [
        "llava",
        "bakllava",
        "moondream",
        "llama3.2-vision",
        "llama3.2vision",
        "minicpm-v",
        "gemma3",
        "gemma4"
    ];

    public static bool SupportsImages(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        return VisionModelNameFragments.Any(fragment =>
            model.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}
