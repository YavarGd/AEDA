using Microsoft.Win32;
using System.Runtime.Versioning;

namespace PersonalAI.Desktop.Presentation.Services;

[SupportedOSPlatform("windows")]
public interface IWindowsStartupRegistry
{
    WindowsStartupRegistryValue? ReadValue(string valueName);

    void SetValue(string valueName, WindowsStartupRegistryValue value);

    void DeleteValue(string valueName);
}

[SupportedOSPlatform("windows")]
public sealed record WindowsStartupRegistryValue(
    object Data,
    RegistryValueKind Kind);

[SupportedOSPlatform("windows")]
public sealed record WindowsStartupRegistrationSnapshot(
    WindowsStartupRegistryValue? ProductionValue,
    WindowsStartupRegistryValue? LegacyWinUiValue);

/// <summary>
/// Owns the AEDA Windows Run value. The retained WinUI shell intentionally supplies no
/// executable path, so it cannot overwrite the Avalonia production registration.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string ProductionRunValueName = "AEDA";
    public const string LegacyWinUiRunValueName = "PersonalAI.WinUI";

    private readonly IWindowsStartupRegistry _registry;
    private readonly Func<string?> _productionExecutablePath;

    public WindowsStartupRegistrationService()
        : this(
            new CurrentUserWindowsStartupRegistry(),
            () => ResolveExecutablePath(Environment.ProcessPath))
    {
    }

    public WindowsStartupRegistrationService(Func<string?> productionExecutablePath)
        : this(new CurrentUserWindowsStartupRegistry(), productionExecutablePath)
    {
    }

    public WindowsStartupRegistrationService(
        IWindowsStartupRegistry registry,
        Func<string?> productionExecutablePath)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _productionExecutablePath = productionExecutablePath ??
            throw new ArgumentNullException(nameof(productionExecutablePath));
    }

    public bool IsSupported => GetRunCommand() is not null;

    public bool IsEnabled() =>
        _registry.ReadValue(ProductionRunValueName)?.Data is string value &&
        !string.IsNullOrWhiteSpace(value);

    public StartupRegistrationResult SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                _registry.DeleteValue(ProductionRunValueName);
                return new StartupRegistrationResult(
                    true,
                    "AEDA will not launch at Windows sign-in.");
            }

            var command = GetRunCommand();
            if (command is null)
            {
                return new StartupRegistrationResult(
                    false,
                    "Launch at sign-in is unavailable for this host.");
            }

            _registry.SetValue(
                ProductionRunValueName,
                new WindowsStartupRegistryValue(command, RegistryValueKind.String));
            return new StartupRegistrationResult(
                true,
                "AEDA will launch at Windows sign-in.");
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException ||
            exception is System.Security.SecurityException ||
            exception is IOException)
        {
            return new StartupRegistrationResult(false, exception.Message);
        }
    }

    public WindowsStartupRegistrationSnapshot CaptureCutoverSnapshot() =>
        new(
            _registry.ReadValue(ProductionRunValueName),
            _registry.ReadValue(LegacyWinUiRunValueName));

    public StartupRegistrationResult FinalizeLegacyRemovalAfterVerifiedAvaloniaLaunch(
        bool avaloniaLaunchVerified)
    {
        if (!avaloniaLaunchVerified)
        {
            return new StartupRegistrationResult(
                false,
                "AEDA launch is not verified; the WinUI startup registration was preserved.");
        }

        var command = GetRunCommand();
        var registeredValue = _registry.ReadValue(ProductionRunValueName);
        if (command is null ||
            registeredValue is not
            {
                Data: string registeredCommand,
                Kind: RegistryValueKind.String
            } ||
            !string.Equals(registeredCommand, command, StringComparison.Ordinal))
        {
            return new StartupRegistrationResult(
                false,
                "The verified Avalonia startup command is not registered; the WinUI registration was preserved.");
        }

        try
        {
            _registry.DeleteValue(LegacyWinUiRunValueName);
            return new StartupRegistrationResult(
                true,
                "The legacy WinUI startup registration was removed after Avalonia verification.");
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException ||
            exception is System.Security.SecurityException ||
            exception is IOException)
        {
            return new StartupRegistrationResult(false, exception.Message);
        }
    }

    public StartupRegistrationResult RestoreCutoverSnapshot(
        WindowsStartupRegistrationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            RestoreValue(ProductionRunValueName, snapshot.ProductionValue);
            RestoreValue(LegacyWinUiRunValueName, snapshot.LegacyWinUiValue);
            return new StartupRegistrationResult(
                true,
                "Windows startup registration was restored to its pre-CUT state.");
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException ||
            exception is System.Security.SecurityException ||
            exception is IOException)
        {
            return new StartupRegistrationResult(false, exception.Message);
        }
    }

    public static string? ResolveExecutablePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ||
        path.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            ? null
            : path;

    private string? GetRunCommand()
    {
        var executablePath = _productionExecutablePath();
        return string.IsNullOrWhiteSpace(executablePath)
            ? null
            : $"\"{executablePath}\"";
    }

    private void RestoreValue(string valueName, WindowsStartupRegistryValue? value)
    {
        if (value is null)
        {
            _registry.DeleteValue(valueName);
            return;
        }

        _registry.SetValue(valueName, value);
    }

    private sealed class CurrentUserWindowsStartupRegistry : IWindowsStartupRegistry
    {
        public WindowsStartupRegistryValue? ReadValue(string valueName)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(
                valueName,
                defaultValue: null,
                RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value is null || key is null
                ? null
                : new WindowsStartupRegistryValue(value, key.GetValueKind(valueName));
        }

        public void SetValue(string valueName, WindowsStartupRegistryValue value)
        {
            ArgumentNullException.ThrowIfNull(value);
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(valueName, value.Data, value.Kind);
        }

        public void DeleteValue(string valueName)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}
