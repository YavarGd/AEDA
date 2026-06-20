using PersonalAI.Core.Coding;

namespace PersonalAI.Infrastructure.Coding;

public sealed class ValidationPlanService : IValidationPlanService
{
    public PatchProposalValidationPlan CreatePlan(
        IReadOnlyList<PatchProposalFile> files)
    {
        var commands = new List<PatchProposalValidationCommand>();
        if (files.Any(file => file.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            commands.Add(new PatchProposalValidationCommand(
                @"dotnet test PersonalAI.Tests\PersonalAI.Tests.csproj",
                "Run the deterministic backend test suite."));
        }

        commands.Add(new PatchProposalValidationCommand(
            "dotnet build PersonalAI.slnx",
            "Verify the Debug solution build."));
        commands.Add(new PatchProposalValidationCommand(
            "dotnet build PersonalAI.slnx -c Release",
            "Verify the Release solution build."));

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file.RelativePath);
            if (file.RelativePath.Contains("Tests/", StringComparison.OrdinalIgnoreCase) ||
                file.RelativePath.Contains("Tests\\", StringComparison.OrdinalIgnoreCase))
            {
                commands.Insert(0, new PatchProposalValidationCommand(
                    $@"dotnet test PersonalAI.Tests\PersonalAI.Tests.csproj --filter {name}",
                    "Run the targeted test related to the changed test file."));
                break;
            }
        }

        return new PatchProposalValidationPlan(
            commands
                .Where(command => !IsDestructive(command.Command))
                .DistinctBy(command => command.Command)
                .Take(5)
                .ToArray(),
            ["Inspect the generated diff before any future apply operation."]);
    }

    private static bool IsDestructive(string command) =>
        command.Contains(" rm ", StringComparison.OrdinalIgnoreCase) ||
        command.Contains("del ", StringComparison.OrdinalIgnoreCase) ||
        command.Contains("git reset", StringComparison.OrdinalIgnoreCase);
}
