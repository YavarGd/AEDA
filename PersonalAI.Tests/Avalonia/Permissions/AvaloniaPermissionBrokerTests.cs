using System.Runtime.CompilerServices;
using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Reference;
using PersonalAI.Desktop.Avalonia.Composition;

namespace PersonalAI.Tests.Avalonia.Permissions;

public sealed class AvaloniaPermissionDialogBrokerTests
{
    [Fact]
    public async Task MissingOwnerFailsClosed()
    {
        using var broker = new AvaloniaPermissionBroker(() => null);

        var response = await broker.RequestPermissionAsync(CreateRequest());

        Assert.Equal(PermissionDecision.Deny, response.Decision);
    }

    [Fact]
    public async Task OwnerLookupFailureFailsClosed()
    {
        using var broker = new AvaloniaPermissionBroker(
            () => throw new InvalidOperationException("No window root."));

        var response = await broker.RequestPermissionAsync(CreateRequest());

        Assert.Equal(PermissionDecision.Deny, response.Decision);
    }

    [Fact]
    public void DialogKeepsScopeTechnicalDetailsAndExplicitDecisionsVisible()
    {
        var source = ReadAvaloniaSource(
            "Views",
            "Dialogs",
            "AvaloniaPermissionDialog.cs");

        Assert.Contains("Target scope", source);
        Assert.Contains("Technical details", source);
        Assert.Contains("Allow once", source);
        Assert.Contains("Allow for this task", source);
        Assert.Contains("Deny", source);
        Assert.Contains("Cancel task", source);
    }

    [Fact]
    public void CompositionUsesTheAvaloniaBrokerWithoutChangingTheAppRoot()
    {
        var composition = ReadAvaloniaSource(
            "Composition",
            "AvaloniaAppComposition.cs");
        var app = ReadAvaloniaSource("App.axaml.cs");

        Assert.Contains("new AvaloniaPermissionBroker", composition);
        Assert.Contains("AedaRuntime.CreateAsync(permissionBroker)", composition);
        Assert.DoesNotContain("DenyingPermissionBroker", composition);
        Assert.DoesNotContain("Permission", app, StringComparison.Ordinal);
    }

    private static PermissionRequest CreateRequest() =>
        new(
            Guid.NewGuid(),
            TaskId.NewId(),
            GetCurrentUtcTimeTool.Id,
            "Current time",
            [ToolPermission.ReadSystemTime],
            PermissionRiskLevel.Low,
            "Read the system clock.",
            "clock",
            PermissionAccessMode.Read);

    private static string ReadAvaloniaSource(params string[] relativePath)
    {
        var repositoryRoot = GetRepositoryRoot();
        var path = Path.Combine(
            [repositoryRoot, "PersonalAI.Desktop.Avalonia", .. relativePath]);
        Assert.True(File.Exists(path), $"Avalonia source not found at {path}");
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot(
        [CallerFilePath] string testFilePath = "")
    {
        var directory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(directory, "..", "..", ".."));
    }
}
