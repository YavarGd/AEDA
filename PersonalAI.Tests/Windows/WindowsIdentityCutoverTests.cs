using System.Runtime.CompilerServices;
using Microsoft.Win32;
using PersonalAI.Desktop.Presentation.Services;
using PersonalAI.Infrastructure.Persistence;
using PersonalAI.Infrastructure.Settings;
using PersonalAI.Infrastructure.Windows;

namespace PersonalAI.Tests.Windows;

public sealed class WindowsIdentityCutoverTests
{
    [Fact]
    public void AvaloniaIsTheProductionExecutableAndDefaultLaunchTarget()
    {
        var project = ReadSource(
            "PersonalAI.Desktop.Avalonia",
            "PersonalAI.Desktop.Avalonia.csproj");
        var composition = ReadSource(
            "PersonalAI.Desktop.Avalonia",
            "Composition",
            "AvaloniaAppComposition.cs");
        var launcher = ReadSource("tools", "Install-AedaStartMenuShortcut.ps1");
        var readme = ReadSource("README.md");

        Assert.Contains("<AssemblyName>AEDA</AssemblyName>", project);
        Assert.Contains("<Product>AEDA</Product>", project);
        Assert.Contains("new WindowsStartupRegistrationService()", composition);
        Assert.DoesNotContain("DeferredStartupRegistrationService", composition);
        var rollbackRegistration = ReadSource(
            "PersonalAI.Desktop.WinUI",
            "Services",
            "WindowsStartupRegistrationService.cs");
        Assert.Contains("new(() => null)", rollbackRegistration);
        Assert.Contains("PersonalAI.Desktop.Avalonia", launcher);
        Assert.Contains("AEDA.exe", launcher);
        Assert.Contains(
            "dotnet run --project PersonalAI.Desktop.Avalonia/PersonalAI.Desktop.Avalonia.csproj",
            readme);
    }

    [Fact]
    public void BothWindowsBinariesUseTheSharedAedaMutexAndBoundedActivation()
    {
        var avaloniaProgram = ReadSource("PersonalAI.Desktop.Avalonia", "Program.cs");
        var winUiApp = ReadSource("PersonalAI.Desktop.WinUI", "App.xaml.cs");

        Assert.Equal("Local\\AEDA.SingleInstance", WindowsSingleInstanceService.MutexName);
        Assert.Contains("new WindowsSingleInstanceService()", avaloniaProgram);
        Assert.Contains("new WindowsSingleInstanceService()", winUiApp);
        Assert.Contains("PersonalAiActivationClient.TryActivatePrimaryAsync()", avaloniaProgram);
        Assert.Contains("PersonalAiActivationClient.TryActivatePrimaryAsync()", winUiApp);
        Assert.DoesNotContain("PersonalAI.WinUI.SingleInstance", avaloniaProgram + winUiApp);
    }

    [Fact]
    public void StartupRegistrationResolvesToTheAvaloniaExecutable()
    {
        var registry = new FakeWindowsStartupRegistry();
        var service = CreateService(registry);

        var result = service.SetEnabled(true);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(
            new WindowsStartupRegistryValue(
                "\"C:\\AEDA\\AEDA.exe\"",
                RegistryValueKind.String),
            registry.ReadValue(WindowsStartupRegistrationService.ProductionRunValueName));
        Assert.Null(registry.ReadValue(WindowsStartupRegistrationService.LegacyWinUiRunValueName));
    }

    [Fact]
    public void FailedAvaloniaVerificationDoesNotRemoveLegacyWinUiRegistration()
    {
        var registry = new FakeWindowsStartupRegistry();
        var legacy = new WindowsStartupRegistryValue(
            "\"C:\\PersonalAI\\PersonalAI.Desktop.WinUI.exe\" --background",
            RegistryValueKind.ExpandString);
        registry.SetValue(WindowsStartupRegistrationService.LegacyWinUiRunValueName, legacy);
        var service = CreateService(registry);

        var result = service.FinalizeLegacyRemovalAfterVerifiedAvaloniaLaunch(false);

        Assert.False(result.Succeeded);
        Assert.Equal(legacy, registry.ReadValue(
            WindowsStartupRegistrationService.LegacyWinUiRunValueName));
        Assert.Null(registry.ReadValue(WindowsStartupRegistrationService.ProductionRunValueName));
    }

    [Fact]
    public void VerifiedAvaloniaRegistrationPermitsLegacyWinUiRemoval()
    {
        var registry = new FakeWindowsStartupRegistry();
        registry.SetValue(
            WindowsStartupRegistrationService.LegacyWinUiRunValueName,
            new WindowsStartupRegistryValue(
                "\"C:\\PersonalAI\\PersonalAI.Desktop.WinUI.exe\"",
                RegistryValueKind.String));
        var service = CreateService(registry);

        Assert.True(service.SetEnabled(true).Succeeded);
        var result = service.FinalizeLegacyRemovalAfterVerifiedAvaloniaLaunch(true);

        Assert.True(result.Succeeded, result.Message);
        Assert.Null(registry.ReadValue(
            WindowsStartupRegistrationService.LegacyWinUiRunValueName));
    }

    [Fact]
    public void RollbackRestoresTheExactPriorStartupValues()
    {
        var registry = new FakeWindowsStartupRegistry();
        var priorProduction = new WindowsStartupRegistryValue(
            "\"C:\\OldAEDA\\AEDA.exe\" --restore",
            RegistryValueKind.ExpandString);
        var priorWinUi = new WindowsStartupRegistryValue(
            "\"C:\\PersonalAI\\PersonalAI.Desktop.WinUI.exe\" --background",
            RegistryValueKind.String);
        registry.SetValue(WindowsStartupRegistrationService.ProductionRunValueName, priorProduction);
        registry.SetValue(WindowsStartupRegistrationService.LegacyWinUiRunValueName, priorWinUi);
        var service = CreateService(registry);
        var snapshot = service.CaptureCutoverSnapshot();

        Assert.True(service.SetEnabled(true).Succeeded);
        Assert.True(service.FinalizeLegacyRemovalAfterVerifiedAvaloniaLaunch(true).Succeeded);
        var result = service.RestoreCutoverSnapshot(snapshot);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(priorProduction, registry.ReadValue(
            WindowsStartupRegistrationService.ProductionRunValueName));
        Assert.Equal(priorWinUi, registry.ReadValue(
            WindowsStartupRegistrationService.LegacyWinUiRunValueName));
    }

    [Fact]
    public void RepeatedCutAndRollbackAreIdempotent()
    {
        var registry = new FakeWindowsStartupRegistry();
        var priorWinUi = new WindowsStartupRegistryValue(
            "\"C:\\PersonalAI\\PersonalAI.Desktop.WinUI.exe\"",
            RegistryValueKind.String);
        registry.SetValue(WindowsStartupRegistrationService.LegacyWinUiRunValueName, priorWinUi);
        var service = CreateService(registry);
        var snapshot = service.CaptureCutoverSnapshot();

        Assert.True(service.SetEnabled(true).Succeeded);
        Assert.True(service.SetEnabled(true).Succeeded);
        Assert.True(service.FinalizeLegacyRemovalAfterVerifiedAvaloniaLaunch(true).Succeeded);
        Assert.True(service.FinalizeLegacyRemovalAfterVerifiedAvaloniaLaunch(true).Succeeded);
        Assert.True(service.RestoreCutoverSnapshot(snapshot).Succeeded);
        Assert.True(service.RestoreCutoverSnapshot(snapshot).Succeeded);

        Assert.Null(registry.ReadValue(WindowsStartupRegistrationService.ProductionRunValueName));
        Assert.Equal(priorWinUi, registry.ReadValue(
            WindowsStartupRegistrationService.LegacyWinUiRunValueName));
    }

    [Fact]
    public void ProfileAndConversationPathsRemainUnchanged()
    {
        Assert.EndsWith(
            Path.Combine("PersonalAI", "settings.json"),
            ApplicationSettingsPaths.GetDefaultSettingsPath(),
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("PersonalAI", "personalai.db"),
            ConversationDatabasePaths.GetDefaultDatabasePath(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static WindowsStartupRegistrationService CreateService(
        IWindowsStartupRegistry registry) =>
        new(registry, () => @"C:\AEDA\AEDA.exe");

    private static string ReadSource(params string[] pathParts)
    {
        var testFilePath = GetTestFilePath();
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(testFilePath)!,
            "..",
            ".."));
        return File.ReadAllText(Path.Combine([repositoryRoot, .. pathParts]));
    }

    private static string GetTestFilePath(
        [CallerFilePath] string testFilePath = "") => testFilePath;

    private sealed class FakeWindowsStartupRegistry : IWindowsStartupRegistry
    {
        private readonly Dictionary<string, WindowsStartupRegistryValue> _values =
            new(StringComparer.Ordinal);

        public WindowsStartupRegistryValue? ReadValue(string valueName) =>
            _values.TryGetValue(valueName, out var value) ? value : null;

        public void SetValue(string valueName, WindowsStartupRegistryValue value) =>
            _values[valueName] = value;

        public void DeleteValue(string valueName) => _values.Remove(valueName);
    }
}
