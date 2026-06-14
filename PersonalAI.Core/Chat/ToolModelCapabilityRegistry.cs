namespace PersonalAI.Core.Chat;

public static class ToolModelCapabilityRegistry
{
    private static readonly string[] KnownToolCapablePatterns =
    [
        "qwen2.5",
        "qwen3",
        "qwen3-vl",
        "gemma4",
        "llama3.1",
        "llama3.2",
        "mistral",
        "mistral-nemo"
    ];

    public static bool SupportsTools(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        return KnownToolCapablePatterns.Any(pattern =>
            model.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
