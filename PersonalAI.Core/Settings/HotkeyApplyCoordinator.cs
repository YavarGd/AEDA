namespace PersonalAI.Core.Settings;

public sealed record HotkeyApplyResult(
    bool Succeeded,
    HotkeySettings AcceptedHotkey,
    string Message);

public static class HotkeyApplyCoordinator
{
    public static async Task<HotkeyApplyResult> ApplyAsync(
        HotkeySettings draft,
        HotkeySettings current,
        Func<HotkeySettings, CancellationToken, Task<bool>> tryRegisterAsync,
        Func<HotkeySettings, CancellationToken, Task> saveAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tryRegisterAsync);
        ArgumentNullException.ThrowIfNull(saveAsync);

        var validation = HotkeySettingsValidator.Validate(draft);

        if (!validation.IsValid)
        {
            return new HotkeyApplyResult(
                false,
                current,
                validation.ErrorMessage ?? "Invalid hotkey.");
        }

        if (!await tryRegisterAsync(validation.Normalized, cancellationToken))
        {
            return new HotkeyApplyResult(
                false,
                current,
                "Hotkey is unavailable; keeping the previous shortcut.");
        }

        await saveAsync(validation.Normalized, cancellationToken);
        return new HotkeyApplyResult(
            true,
            validation.Normalized,
            $"Hotkey set to {HotkeySettingsValidator.Format(validation.Normalized)}.");
    }
}
