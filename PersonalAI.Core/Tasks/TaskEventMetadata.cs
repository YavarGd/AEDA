namespace PersonalAI.Core.Tasks;

public static class TaskEventMetadata
{
    public const int MaxMetadataKeyLength = 64;
    public const int MaxMetadataValueLength = 256;
    public const int MaxProgressLabelLength = 128;
    public const int MaxErrorCodeLength = 64;

    public static IReadOnlyDictionary<string, string> CreateSafe(
        params (string Key, string Value)[] values)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                key.Length > MaxMetadataKeyLength ||
                ContainsSecretMarker(key))
            {
                throw new ArgumentException(
                    "Metadata keys must be short, explicit, and non-secret.");
            }

            metadata[key] = SanitizeValue(value);
        }

        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            metadata);
    }

    public static IReadOnlyDictionary<string, string>? Normalize(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        return CreateSafe(metadata.Select(pair => (pair.Key, pair.Value)).ToArray());
    }

    public static string SanitizeSummary(string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        return SanitizeValue(summary, maxLength: 512);
    }

    public static string? SanitizeProgressLabel(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return SanitizeValue(value, MaxProgressLabelLength);
    }

    public static string? SanitizeErrorCode(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length == 0 ||
            value.Length > MaxErrorCodeLength ||
            value.Any(character =>
                !(char.IsLower(character) ||
                  char.IsDigit(character) ||
                  character is '_' or '-' or '.')))
        {
            throw new ArgumentException(
                "Safe error codes must be short lowercase identifiers.",
                nameof(value));
        }

        return value;
    }

    private static string SanitizeValue(
        string? value,
        int maxLength = MaxMetadataValueLength)
    {
        var sanitized = value ?? string.Empty;

        if (ContainsSecretMarker(sanitized))
        {
            sanitized = "[redacted]";
        }

        sanitized = sanitized.ReplaceLineEndings(" ");

        return sanitized.Length <= maxLength
            ? sanitized
            : sanitized[..maxLength];
    }

    private static bool ContainsSecretMarker(string value) =>
        value.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("access_token", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("token=", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("secret", StringComparison.OrdinalIgnoreCase);
}
