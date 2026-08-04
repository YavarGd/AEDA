using System.Runtime.CompilerServices;

namespace PersonalAI.Tests.Avalonia.Code;

public sealed class AvaloniaCodeViewTests
{
    [Fact]
    public void ViewExposesTheSupervisedWorkflowWithoutSafetyLogic()
    {
        var source = ReadAvaloniaSource("Views", "Code", "CodeView.axaml");

        Assert.Contains("CreateProposalCommand", source);
        Assert.Contains("CancelProposalCreationCommand", source);
        Assert.Contains("DryRunSelectedProposalCommand", source);
        Assert.Contains("RequestApplyApprovalCommand", source);
        Assert.Contains("ApplyApprovedProposalCommand", source);
        Assert.Contains("StaleProposalRecoveryText", source);
        Assert.Contains("RunApprovedValidationCommand", source);
        Assert.Contains("RollbackSelectedApplyResultCommand", source);
        Assert.Contains("CodeTimelineGroups", source);
        Assert.Contains("Bounded unified diff preview", source);
        Assert.DoesNotContain("PatchApplyService", source);
        Assert.DoesNotContain("ValidationRunnerService", source);
    }

    [Fact]
    public void ScreenUsesTheExistingPresentationViewModelAndE1Registration()
    {
        var view = ReadAvaloniaSource("Views", "Code", "CodeView.axaml");
        var composition = ReadAvaloniaSource("Composition", "AvaloniaAppComposition.cs");
        var app = ReadAvaloniaSource("App.axaml.cs");

        Assert.Contains("x:DataType=\"vm:AedaCodeModuleViewModel\"", view);
        Assert.Contains("\"aeda-code\"", composition);
        Assert.Contains("new AedaCodeModuleViewModel", composition);
        Assert.DoesNotContain("AedaCode", app);
    }

    [Fact]
    public void SafetyStatusAndPrimaryControlsAreAccessible()
    {
        var source = ReadAvaloniaSource("Views", "Code", "CodeView.axaml");

        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", source);
        Assert.Contains("AutomationProperties.Name=\"Code workspace\"", source);
        Assert.Contains("AutomationProperties.Name=\"Code change request\"", source);
        Assert.Contains("AutomationProperties.Name=\"Sanitized validation output\"", source);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", source);
    }

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
