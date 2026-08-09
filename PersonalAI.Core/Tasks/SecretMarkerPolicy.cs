namespace PersonalAI.Core.Tasks;

/// <summary>
/// The project's single secret-marker test. Extracted verbatim from the private predicate that
/// <see cref="TaskEventMetadata"/> already applied to task metadata, summaries, and progress
/// labels, so every surface that shows model- or user-authored text redacts on exactly the same
/// terms instead of each carrying its own weaker list.
/// </summary>
public static class SecretMarkerPolicy
{
    /// <summary>The replacement used when a value trips <see cref="ContainsSecretMarker"/>.</summary>
    public const string Redacted = "[redacted]";

    private static readonly string[] Markers =
    [
        "api_key",
        "apikey",
        "access_token",
        "token=",
        "secret"
    ];

    public static bool ContainsSecretMarker(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var marker in Markers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
