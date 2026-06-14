using System.Text;
using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Workspace;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Tools;
using PersonalAI.Infrastructure.Tools.Workspace;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Tests.Workspaces;

public abstract class WorkspaceTestBase : IDisposable
{
    protected WorkspaceTestBase()
    {
        Root = Path.Combine(Path.GetTempPath(), $"personalai-workspace-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        Registry = new WorkspaceRegistry();
        Options = new WorkspaceToolOptions();
        Resolver = new WorkspacePathResolver(Registry);
        Reader = new FileSystemWorkspaceReader(Registry, Resolver, Options);
        Workspace = Registry.Register(Root, "Test workspace", "test");
    }

    protected string Root { get; }

    protected WorkspaceRegistry Registry { get; }

    protected WorkspaceToolOptions Options { get; }

    protected WorkspacePathResolver Resolver { get; }

    protected FileSystemWorkspaceReader Reader { get; }

    protected WorkspaceDescriptor Workspace { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch
        {
        }
    }

    protected string WriteFile(
        string relativePath,
        string content,
        Encoding? encoding = null)
    {
        var fullPath = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content, encoding ?? new UTF8Encoding(false));
        return fullPath;
    }

    protected string WriteBytes(string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
        return fullPath;
    }

    protected TypedToolRuntime CreateRuntime(
        IPermissionBroker broker,
        params ITypedTool[] tools)
    {
        var registry = new TypedToolRegistry();
        foreach (var tool in tools)
        {
            registry.Register(tool);
        }

        return new TypedToolRuntime(
            registry,
            new TaskEventBus(),
            broker,
            new ToolRuntimeOptions(TimeSpan.FromSeconds(5), UsePerTaskPermissionCache: true));
    }

    protected GetWorkspaceInfoTool CreateInfoTool() =>
        new(Reader, Resolver, Options);

    protected ListDirectoryTool CreateListTool() =>
        new(Reader, Resolver, Options);

    protected ReadTextFileTool CreateReadTool() =>
        new(Reader, Resolver, Options);

    protected SearchWorkspaceTextTool CreateSearchTool() =>
        new(Reader, Resolver, Options);

    protected sealed class FixedPermissionBroker(PermissionDecision decision)
        : IPermissionBroker
    {
        public int RequestCount { get; private set; }

        public List<PermissionRequest> Requests { get; } = [];

        public ValueTask<PermissionResponse> RequestPermissionAsync(
            PermissionRequest request,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            Requests.Add(request);
            var response = decision switch
            {
                PermissionDecision.AllowForTask => PermissionResponse.AllowForTask(request),
                PermissionDecision.Deny => PermissionResponse.Deny(request),
                PermissionDecision.CancelTask => PermissionResponse.CancelTask(request),
                _ => PermissionResponse.AllowOnce(request)
            };
            return ValueTask.FromResult(response);
        }
    }
}
