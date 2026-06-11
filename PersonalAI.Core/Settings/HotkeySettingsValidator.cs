namespace PersonalAI.Core.Settings;

public sealed record HotkeyValidationResult(
    bool IsValid,
    HotkeySettings Normalized,
    string? ErrorMessage);

public static class HotkeySettingsValidator
{
    private static readonly HashSet<string> UnsupportedKeys = new(
        ["Control", "Ctrl", "Alt", "Shift", "Windows", "Win"],
        StringComparer.OrdinalIgnoreCase);

    public static HotkeySettings Normalize(HotkeySettings hotkey)
    {
        var key = NormalizeKey(hotkey.Key);

        return hotkey with { Key = key };
    }

    public static HotkeyValidationResult Validate(HotkeySettings hotkey)
    {
        var normalized = Normalize(hotkey);

        if (!normalized.Control &&
            !normalized.Alt &&
            !normalized.Shift &&
            !normalized.Windows)
        {
            return new HotkeyValidationResult(
                false,
                normalized,
                "Choose at least one modifier.");
        }

        if (string.IsNullOrWhiteSpace(normalized.Key))
        {
            return new HotkeyValidationResult(
                false,
                normalized,
                "Choose a main key.");
        }

        if (UnsupportedKeys.Contains(normalized.Key))
        {
            return new HotkeyValidationResult(
                false,
                normalized,
                "Modifier-only shortcuts are not supported.");
        }

        return new HotkeyValidationResult(true, normalized, null);
    }

    public static string Format(HotkeySettings hotkey)
    {
        var normalized = Normalize(hotkey);
        var parts = new List<string>();

        if (normalized.Control)
        {
            parts.Add("Ctrl");
        }

        if (normalized.Alt)
        {
            parts.Add("Alt");
        }

        if (normalized.Shift)
        {
            parts.Add("Shift");
        }

        if (normalized.Windows)
        {
            parts.Add("Win");
        }

        parts.Add(normalized.Key);
        return string.Join("+", parts);
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var trimmed = key.Trim();
        return trimmed.Equals(" ", StringComparison.Ordinal)
            ? "Space"
            : string.Concat(
                trimmed[..1].ToUpperInvariant(),
                trimmed.Length == 1
                    ? string.Empty
                    : trimmed[1..]);
    }
}
