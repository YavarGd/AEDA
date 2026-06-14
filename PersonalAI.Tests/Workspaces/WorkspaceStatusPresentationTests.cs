using PersonalAI.Core.Workspaces;

namespace PersonalAI.Tests.Workspaces;

public sealed class WorkspaceStatusPresentationTests
{
    [Theory]
    [InlineData(WorkspaceRegistrationStatus.Available, null, "Available", "Workspace is available.")]
    [InlineData(WorkspaceRegistrationStatus.Missing, "workspace_not_found", "Unavailable", "The folder no longer exists or cannot be accessed.")]
    [InlineData(WorkspaceRegistrationStatus.AccessDenied, "workspace_access_denied", "Unavailable", "The folder no longer exists or cannot be accessed.")]
    [InlineData(WorkspaceRegistrationStatus.UnsafeReparsePoint, "reparse_point_rejected", "Invalid", "The workspace path is invalid or unsafe.")]
    [InlineData(WorkspaceRegistrationStatus.NeedsReview, "canonical_path_changed", "Invalid", "The workspace path is invalid or unsafe.")]
    [InlineData(WorkspaceRegistrationStatus.ValidationFailed, "workspace_runtime_registration_failed", "Runtime registration failed", "Runtime access could not be registered. Try revalidating.")]
    public void Map_KnownStatesToSafeUserMessages(
        WorkspaceRegistrationStatus status,
        string? safeStatusCode,
        string expectedLabel,
        string expectedMessage)
    {
        var presentation = WorkspaceStatusPresentationMapper.Map(
            CreateWorkspace(status, safeStatusCode));

        Assert.Equal(expectedLabel, presentation.Label);
        Assert.Equal(expectedMessage, presentation.Message);
    }

    [Fact]
    public void Map_UnknownValidationCodeUsesGenericSafeMessage()
    {
        var presentation = WorkspaceStatusPresentationMapper.Map(
            CreateWorkspace(
                WorkspaceRegistrationStatus.ValidationFailed,
                @"System.Exception: C:\Users\secret"));

        Assert.Equal("Invalid", presentation.Label);
        Assert.Equal("Workspace validation failed.", presentation.Message);
        Assert.DoesNotContain("System.Exception", presentation.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", presentation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_RemovedWorkspaceIsClearlyLabeled()
    {
        var workspace = CreateWorkspace(
            WorkspaceRegistrationStatus.Removed,
            null) with
        {
            RemovedAtUtc = DateTimeOffset.UtcNow
        };

        var presentation = WorkspaceStatusPresentationMapper.Map(workspace);

        Assert.Equal("Removed", presentation.Label);
        Assert.Equal("This workspace has been removed.", presentation.Message);
    }

    private static PersistedWorkspace CreateWorkspace(
        WorkspaceRegistrationStatus status,
        string? safeStatusCode)
    {
        var now = DateTimeOffset.UtcNow;
        return new PersistedWorkspace(
            WorkspaceId.NewId(),
            "Workspace",
            @"C:\Workspace",
            "test",
            now,
            now,
            status,
            safeStatusCode,
            true);
    }
}
