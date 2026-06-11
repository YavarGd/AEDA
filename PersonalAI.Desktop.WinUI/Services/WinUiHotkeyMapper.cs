using PersonalAI.Core.Settings;

namespace PersonalAI.Desktop.WinUI.Services;

public readonly record struct WinUiHotkey(uint Modifiers, uint VirtualKey);

public static class WinUiHotkeyMapper
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    public static bool TryMap(
        HotkeySettings settings,
        out WinUiHotkey hotkey,
        out string errorMessage)
    {
        var validation = HotkeySettingsValidator.Validate(settings);

        if (!validation.IsValid)
        {
            hotkey = default;
            errorMessage = validation.ErrorMessage ?? "Invalid hotkey.";
            return false;
        }

        if (!TryMapKey(validation.Normalized.Key, out var virtualKey))
        {
            hotkey = default;
            errorMessage = $"Unsupported hotkey key '{validation.Normalized.Key}'.";
            return false;
        }

        var modifiers = 0u;
        modifiers |= validation.Normalized.Alt ? ModAlt : 0;
        modifiers |= validation.Normalized.Control ? ModControl : 0;
        modifiers |= validation.Normalized.Shift ? ModShift : 0;
        modifiers |= validation.Normalized.Windows ? ModWin : 0;
        hotkey = new WinUiHotkey(modifiers, virtualKey);
        errorMessage = string.Empty;
        return true;
    }

    private static bool TryMapKey(string key, out uint virtualKey)
    {
        if (key.Equals("Space", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = 0x20;
            return true;
        }

        if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
        {
            virtualKey = char.ToUpperInvariant(key[0]);
            return true;
        }

        if (key.StartsWith('F') &&
            int.TryParse(key[1..], out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            return true;
        }

        virtualKey = 0;
        return false;
    }
}
