using System.Text.RegularExpressions;

namespace PersonalAI.Infrastructure.Coding;

public static partial class ValidationOutputSanitizer
{
    public static (string Text, bool Truncated) Sanitize(
        string? value,
        int maxCharacters)
    {
        var text = value ?? string.Empty;
        text = ApiKeyPattern().Replace(text, "[REDACTED_SECRET]");
        text = BearerPattern().Replace(text, "Bearer [REDACTED_SECRET]");
        text = SecretReferencePattern().Replace(text, "secret-ref:[REDACTED]");
        text = AppDataPattern().Replace(text, "%APPDATA%\\PersonalAI");

        var truncated = text.Length > maxCharacters;
        if (truncated)
        {
            text = text[..maxCharacters];
        }

        var lines = text.Split('\n');
        if (lines.Length > 200)
        {
            text = string.Join('\n', lines.Take(200));
            truncated = true;
        }

        return (text, truncated);
    }

    [GeneratedRegex(@"sk-[A-Za-z0-9_\-]{6,}", RegexOptions.IgnoreCase)]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9_\-\.=]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"secret-ref:[A-Za-z0-9_\-\/\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex SecretReferencePattern();

    [GeneratedRegex(@"[A-Z]:\\Users\\[^\\\r\n]+\\AppData\\(?:Local|Roaming)\\PersonalAI", RegexOptions.IgnoreCase)]
    private static partial Regex AppDataPattern();
}
