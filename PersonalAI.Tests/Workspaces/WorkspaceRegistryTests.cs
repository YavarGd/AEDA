using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Tests.Workspaces;

public sealed class WorkspaceRegistryTests : WorkspaceTestBase
{
    [Fact]
    public void Register_ValidWorkspace()
    {
        Assert.True(Registry.TryGet(Workspace.Id, out var found));
        Assert.Equal(Workspace.CanonicalRootPath, found.CanonicalRootPath);
        Assert.True(found.Policy.IsReadOnly);
    }

    [Fact]
    public void Register_RejectsNonexistentDirectory()
    {
        var registry = new WorkspaceRegistry();

        var exception = Assert.Throws<WorkspaceAccessException>(
            () => registry.Register(Path.Combine(Root, "missing")));

        Assert.Equal("workspace_not_found", exception.SafeErrorCode);
    }

    [Fact]
    public void Register_RejectsFilePathAsRoot()
    {
        var file = WriteFile("file.txt", "content");
        var registry = new WorkspaceRegistry();

        var exception = Assert.Throws<WorkspaceAccessException>(
            () => registry.Register(file));

        Assert.Equal("workspace_root_is_file", exception.SafeErrorCode);
    }

    [Fact]
    public void Register_EquivalentRootsAreDeduplicated()
    {
        var first = Registry.Register(Root);
        var second = Registry.Register(Root + Path.DirectorySeparatorChar);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void Register_SameRegistrationReturnsOriginalId()
    {
        var first = Registry.Register(Root);
        var second = Registry.Register(Root);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void Register_SeparateRootsRemainSeparate()
    {
        var other = Path.Combine(Root, "other");
        Directory.CreateDirectory(other);

        var workspace = Registry.Register(other);

        Assert.NotEqual(Workspace.Id, workspace.Id);
    }

    [Fact]
    public void Remove_ClearsBothIndexes()
    {
        var workspace = Registry.Register(Root);

        Assert.True(Registry.Remove(workspace.Id));
        Assert.False(Registry.TryGet(workspace.Id, out _));
        Assert.Empty(Registry.List());
    }

    [Fact]
    public void Register_AfterRemovalGetsNewId()
    {
        var first = Registry.Register(Root);
        Assert.True(Registry.Remove(first.Id));

        var second = Registry.Register(Root);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void NormalizeCanonicalRoot_PreservesRepresentativeFilesystemRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            var normalized = WorkspaceRegistry.NormalizeCanonicalRoot(@"C:\");
            Assert.Equal(@"C:\", normalized);
            Assert.NotEqual("C:", normalized);
            return;
        }

        Assert.Equal("/", WorkspaceRegistry.NormalizeCanonicalRoot("/"));
    }

    [Fact]
    public void NormalizeCanonicalRoot_RemovesOrdinaryTrailingSeparators()
    {
        var root = OperatingSystem.IsWindows()
            ? @"C:\Project\"
            : "/tmp/project//";

        var normalized = WorkspaceRegistry.NormalizeCanonicalRoot(root);

        Assert.Equal(
            OperatingSystem.IsWindows() ? @"C:\Project" : "/tmp/project",
            normalized);
    }

    [Fact]
    public void NormalizeCanonicalRoot_RejectsEmptyResult()
    {
        Assert.Throws<WorkspaceAccessException>(
            () => WorkspaceRegistry.NormalizeCanonicalRoot(string.Empty));
    }

    [Fact]
    public void Register_DirectSymlinkRootRejectedWhenSupported()
    {
        var target = Path.Combine(Root, "target");
        Directory.CreateDirectory(target);
        var link = Path.Combine(Root, "link-root");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch
        {
            return;
        }

        var registry = new WorkspaceRegistry();
        var exception = Assert.Throws<WorkspaceAccessException>(
            () => registry.Register(link));

        Assert.Equal("reparse_point_rejected", exception.SafeErrorCode);
    }

    [Fact]
    public void Register_NestedPathUnderSymlinkParentRejectedWhenSupported()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"personalai-registry-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(outside, "child"));
        var link = Path.Combine(Root, "link-parent");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch
        {
            Directory.Delete(outside, recursive: true);
            return;
        }

        try
        {
            var registry = new WorkspaceRegistry();
            var exception = Assert.Throws<WorkspaceAccessException>(
                () => registry.Register(Path.Combine(link, "child")));

            Assert.Equal("reparse_point_rejected", exception.SafeErrorCode);
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Register_NormalNestedPathAccepted()
    {
        var nested = Path.Combine(Root, "a", "b");
        Directory.CreateDirectory(nested);
        var registry = new WorkspaceRegistry();

        var workspace = registry.Register(nested);

        Assert.True(Path.IsPathFullyQualified(workspace.CanonicalRootPath));
    }

    [Fact]
    public void Register_InvalidPathFailuresAreStructuredAndSafe()
    {
        var registry = new WorkspaceRegistry();

        var exception = Assert.Throws<WorkspaceAccessException>(
            () => registry.Register("bad\0root"));

        Assert.Equal("invalid_workspace_root", exception.SafeErrorCode);
        Assert.DoesNotContain("bad", exception.SafeErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_ConcurrentEquivalentRegistrationProducesOneWorkspace()
    {
        var registry = new WorkspaceRegistry();
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => registry.Register(Root)))
            .ToArray();

        var workspaces = await Task.WhenAll(tasks);

        Assert.Single(workspaces.Select(workspace => workspace.Id).Distinct());
        Assert.Single(registry.List());
    }
}
