using PersonalAI.Core.Workspaces;

namespace PersonalAI.Tests.Workspaces;

public sealed class WorkspacePathResolverTests : WorkspaceTestBase
{
    [Fact]
    public void Resolve_NormalRelativeFile()
    {
        WriteFile("README.md", "hello");

        var path = Resolver.Resolve(Workspace.Id, "README.md", WorkspacePathKind.File);

        Assert.Equal("README.md", path.RelativePath);
    }

    [Theory]
    [InlineData("C:foo")]
    [InlineData(@"C:\foo")]
    [InlineData("C:/foo")]
    [InlineData("file.txt:stream")]
    public void Resolve_BlocksWindowsColonFormsOnWindows(string relativePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var exception = Assert.Throws<WorkspaceAccessException>(
            () => Resolver.Resolve(Workspace.Id, relativePath));

        Assert.Equal("invalid_relative_path", exception.SafeErrorCode);
    }

    [Fact]
    public void Resolve_NormalRelativePathStillAllowed()
    {
        WriteFile(Path.Combine("docs", "architecture", "readme.md"), "ok");

        var path = Resolver.Resolve(
            Workspace.Id,
            "docs/architecture/readme.md",
            WorkspacePathKind.File);

        Assert.Equal("docs/architecture/readme.md", path.RelativePath);
    }

    [Fact]
    public void Resolve_RootDirectory()
    {
        var path = Resolver.Resolve(Workspace.Id, ".", WorkspacePathKind.Directory);

        Assert.Equal(".", path.RelativePath);
    }

    [Fact]
    public void Resolve_NestedPath()
    {
        WriteFile(Path.Combine("src", "App.cs"), "class App {}");

        var path = Resolver.Resolve(Workspace.Id, @"src\App.cs", WorkspacePathKind.File);

        Assert.Equal("src/App.cs", path.RelativePath);
    }

    [Theory]
    [InlineData(@"..\secret.txt")]
    [InlineData(@"sub\..\..\secret.txt")]
    public void Resolve_BlocksTraversal(string relativePath)
    {
        var exception = Assert.Throws<WorkspaceAccessException>(
            () => Resolver.Resolve(Workspace.Id, relativePath));

        Assert.Equal("path_outside_workspace", exception.SafeErrorCode);
    }

    [Fact]
    public void Resolve_BlocksAbsolutePath()
    {
        var exception = Assert.Throws<WorkspaceAccessException>(
            () => Resolver.Resolve(Workspace.Id, Path.GetFullPath(@"C:\Users\secret.txt")));

        Assert.Equal("invalid_relative_path", exception.SafeErrorCode);
    }

    [Fact]
    public void Resolve_BlocksUncPath()
    {
        var exception = Assert.Throws<WorkspaceAccessException>(
            () => Resolver.Resolve(Workspace.Id, @"\\server\share\file.txt"));

        Assert.Equal("invalid_relative_path", exception.SafeErrorCode);
    }

    [Fact]
    public void Resolve_BlocksPrefixConfusion()
    {
        var sibling = Root.TrimEnd(Path.DirectorySeparatorChar) + "Secrets";
        Directory.CreateDirectory(sibling);
        try
        {
            var exception = Assert.Throws<WorkspaceAccessException>(
                () => Resolver.Resolve(Workspace.Id, Path.Combine("..", Path.GetFileName(sibling), "file.txt")));

            Assert.Equal("path_outside_workspace", exception.SafeErrorCode);
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Fact]
    public void Resolve_BlocksNullByte()
    {
        var exception = Assert.Throws<WorkspaceAccessException>(
            () => Resolver.Resolve(Workspace.Id, "a\0b"));

        Assert.Equal("invalid_relative_path", exception.SafeErrorCode);
    }

    [Fact]
    public void Resolve_AnyRejectsMissingFinalTarget()
    {
        var exception = Assert.Throws<WorkspaceAccessException>(
            () => Resolver.Resolve(Workspace.Id, "missing.txt", WorkspacePathKind.Any));

        Assert.Equal("path_not_found", exception.SafeErrorCode);
    }

    [Fact]
    public void Resolve_AnyAllowsExistingFile()
    {
        WriteFile("file.txt", "content");

        var path = Resolver.Resolve(Workspace.Id, "file.txt", WorkspacePathKind.Any);

        Assert.Equal("file.txt", path.RelativePath);
    }

    [Fact]
    public void Resolve_AnyAllowsExistingDirectory()
    {
        Directory.CreateDirectory(Path.Combine(Root, "docs"));

        var path = Resolver.Resolve(Workspace.Id, "docs", WorkspacePathKind.Any);

        Assert.Equal("docs", path.RelativePath);
    }

    [Fact]
    public void Resolve_AnyRejectsMissingIntermediateComponent()
    {
        var exception = Assert.Throws<WorkspaceAccessException>(
            () => Resolver.Resolve(Workspace.Id, Path.Combine("missing", "file.txt")));

        Assert.Equal("path_not_found", exception.SafeErrorCode);
    }

    [Fact]
    public void Resolve_BlocksSymlinkEscapeWhenSupported()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"personalai-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        var link = Path.Combine(Root, "link");
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
            var exception = Assert.Throws<WorkspaceAccessException>(
                () => Resolver.Resolve(Workspace.Id, Path.Combine("link", "secret.txt")));

            Assert.Equal("reparse_point_rejected", exception.SafeErrorCode);
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(outside, recursive: true);
        }
    }
}
