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

public sealed class WorkspaceToolTests : WorkspaceTestBase
{
    [Fact]
    public async Task GetWorkspaceInfoTool_ReturnsBoundedReadOnlyInfo()
    {
        WriteFile("a.txt", "a");
        Directory.CreateDirectory(Path.Combine(Root, "src"));
        var tool = CreateInfoTool();

        var output = await tool.ExecuteAsync(
            new GetWorkspaceInfoInput(Workspace.Id),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        Assert.Equal(Workspace.Id, output.WorkspaceId);
        Assert.True(output.IsReadOnly);
        Assert.True(output.ImmediateFileCount >= 1);
        Assert.True(output.ImmediateDirectoryCount >= 1);
    }

    [Fact]
    public async Task GetWorkspaceInfoTool_UnknownWorkspaceValidationFails()
    {
        var validation = await CreateInfoTool().ValidateAsync(
            new GetWorkspaceInfoInput(new WorkspaceId("missing")));

        Assert.False(validation.IsValid);
        Assert.Equal("workspace_not_found", validation.SafeErrorCode);
    }

    [Fact]
    public async Task ListDirectoryTool_ListsImmediateEntriesSortedAndTruncated()
    {
        Directory.CreateDirectory(Path.Combine(Root, "bDir"));
        WriteFile("z.txt", "z");
        WriteFile("a.txt", "a");
        var output = await CreateListTool().ExecuteAsync(
            new ListDirectoryInput(Workspace.Id, ".", MaxEntries: 2),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        Assert.Equal(2, output.Entries.Count);
        Assert.Equal(WorkspaceEntryType.Directory, output.Entries[0].Type);
        Assert.True(output.IsTruncated);
    }

    [Fact]
    public async Task ListDirectoryTool_HiddenFilesAreExcludedByDefault()
    {
        var file = WriteFile("hidden.txt", "hidden");
        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.Hidden);

        var output = await CreateListTool().ExecuteAsync(
            new ListDirectoryInput(Workspace.Id, "."),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        Assert.DoesNotContain(output.Entries, entry => entry.Name == "hidden.txt");
    }

    [Fact]
    public async Task ListDirectoryTool_NonexistentDirectoryValidationFails()
    {
        var validation = await CreateListTool().ValidateAsync(
            new ListDirectoryInput(Workspace.Id, "missing"));

        Assert.False(validation.IsValid);
        Assert.Equal("directory_not_found", validation.SafeErrorCode);
    }

    [Fact]
    public async Task ListDirectoryTool_FileAsDirectoryValidationFails()
    {
        WriteFile("file.txt", "text");

        var validation = await CreateListTool().ValidateAsync(
            new ListDirectoryInput(Workspace.Id, "file.txt"));

        Assert.False(validation.IsValid);
        Assert.Equal("wrong_target_type", validation.SafeErrorCode);
    }

    [Fact]
    public async Task ReadTextFileTool_ReadsUtf8AndTruncates()
    {
        WriteFile("readme.md", "abcdef");

        var output = await CreateReadTool().ExecuteAsync(
            new ReadTextFileInput(Workspace.Id, "readme.md", MaxCharacters: 3),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        Assert.Equal("abc", output.Content);
        Assert.True(output.IsTruncated);
        Assert.Equal("utf-8", output.EncodingName);
    }

    [Fact]
    public async Task ReadTextFileTool_ReadsUtf8BomAndUtf16Bom()
    {
        WriteFile("utf8bom.txt", "hello", new UTF8Encoding(true));
        WriteFile("utf16.txt", "hello", Encoding.Unicode);
        WriteFile("utf16be.txt", "hello", Encoding.BigEndianUnicode);

        var utf8 = await CreateReadTool().ExecuteAsync(
            new ReadTextFileInput(Workspace.Id, "utf8bom.txt"),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));
        var utf16 = await CreateReadTool().ExecuteAsync(
            new ReadTextFileInput(Workspace.Id, "utf16.txt"),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));
        var utf16be = await CreateReadTool().ExecuteAsync(
            new ReadTextFileInput(Workspace.Id, "utf16be.txt"),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        Assert.Equal("hello", utf8.Content);
        Assert.Equal("hello", utf16.Content);
        Assert.Equal("hello", utf16be.Content);
    }

    [Fact]
    public async Task ReadTextFileTool_EmptyFile()
    {
        WriteFile("empty.txt", string.Empty);

        var output = await CreateReadTool().ExecuteAsync(
            new ReadTextFileInput(Workspace.Id, "empty.txt"),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        Assert.Equal(string.Empty, output.Content);
    }

    [Fact]
    public async Task ReadTextFileTool_BinaryFileValidationThroughRuntimeIsStructured()
    {
        WriteBytes("binary.bin", [0, 1, 2, 3]);
        var runtime = CreateRuntime(
            new FixedPermissionBroker(PermissionDecision.AllowOnce),
            CreateReadTool());

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(
                ReadTextFileTool.Id,
                new ReadTextFileInput(Workspace.Id, "binary.bin")));

        Assert.Equal(ToolExecutionStatus.ToolFailed, result.Status);
        Assert.Equal("binary_file", result.SafeErrorCode);
    }

    [Fact]
    public async Task ReadTextFileTool_InvalidUtf8ReportsDecodingErrors()
    {
        WriteBytes("invalid.txt", [0x68, 0x69, 0xC3, 0x28]);

        var output = await CreateReadTool().ExecuteAsync(
            new ReadTextFileInput(Workspace.Id, "invalid.txt"),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        Assert.True(output.HadDecodingErrors);
        Assert.Contains("hi", output.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadTextFileTool_OversizedFileIsRejected()
    {
        WriteBytes("large.txt", new byte[Options.MaxReadableFileBytes + 1]);
        var runtime = CreateRuntime(
            new FixedPermissionBroker(PermissionDecision.AllowOnce),
            CreateReadTool());

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(
                ReadTextFileTool.Id,
                new ReadTextFileInput(Workspace.Id, "large.txt")));

        Assert.Equal("file_too_large", result.SafeErrorCode);
    }

    [Fact]
    public async Task ReadTextFileTool_GrownBeyondLimitBeforeOpenIsRejected()
    {
        WriteBytes("large.txt", new byte[5]);
        var limitedOptions = Options with { MaxReadableFileBytes = 4 };
        var reader = new FileSystemWorkspaceReader(Registry, Resolver, limitedOptions);
        var runtime = CreateRuntime(
            new FixedPermissionBroker(PermissionDecision.AllowOnce),
            new ReadTextFileTool(reader, Resolver, limitedOptions));

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(
                ReadTextFileTool.Id,
                new ReadTextFileInput(Workspace.Id, "large.txt")));

        Assert.Equal("file_too_large", result.SafeErrorCode);
    }

    [Fact]
    public void ReadTextFile_MissingFileReturnsStructuredSafeFailure()
    {
        var exception = Assert.Throws<WorkspaceAccessException>(
            () => Reader.ReadTextFile(Workspace.Id, "missing.txt", Options.MaxReadCharacters));

        Assert.Equal("file_not_found", exception.SafeErrorCode);
        Assert.DoesNotContain("missing.txt", exception.SafeErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadTextFile_CancellationIsObservedDuringRead()
    {
        WriteFile("a.txt", "hello");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => Reader.ReadTextFile(
                Workspace.Id,
                "a.txt",
                Options.MaxReadCharacters,
                cancellation.Token));
    }

    [Fact]
    public void ReadTextFile_FinalTargetReparsePointIsRejectedWhenSupported()
    {
        var target = WriteFile("target.txt", "hello");
        var link = Path.Combine(Root, "link.txt");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch
        {
            return;
        }

        var exception = Assert.Throws<WorkspaceAccessException>(
            () => Reader.ReadTextFile(Workspace.Id, "link.txt", Options.MaxReadCharacters));

        Assert.Equal("reparse_point_rejected", exception.SafeErrorCode);
    }

    [Fact]
    public async Task ReadTextFileTool_DirectoryAsFileValidationFails()
    {
        Directory.CreateDirectory(Path.Combine(Root, "dir"));

        var validation = await CreateReadTool().ValidateAsync(
            new ReadTextFileInput(Workspace.Id, "dir"));

        Assert.False(validation.IsValid);
        Assert.Equal("wrong_target_type", validation.SafeErrorCode);
    }

    [Fact]
    public async Task SearchWorkspaceTextTool_FindsLiteralMatches()
    {
        WriteFile(Path.Combine("src", "a.cs"), "Alpha\nneedle here\nNEEDLE");
        WriteFile(Path.Combine("src", "b.md"), "needle again");

        var output = await CreateSearchTool().ExecuteAsync(
            new SearchWorkspaceTextInput(Workspace.Id, "needle", "src"),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        Assert.True(output.Matches.Count >= 3);
        Assert.Contains(output.Matches, match => match.LineNumber == 2);
    }

    [Fact]
    public async Task SearchWorkspaceTextTool_RespectsCaseAndPattern()
    {
        WriteFile("a.cs", "Needle");
        WriteFile("b.md", "Needle");

        var output = await CreateSearchTool().ExecuteAsync(
            new SearchWorkspaceTextInput(
                Workspace.Id,
                "needle",
                ".",
                FilePattern: "*.cs",
                MatchCase: true),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        Assert.Empty(output.Matches);
        Assert.Equal(1, output.FilesScanned);
    }

    [Fact]
    public async Task SearchWorkspaceTextTool_InvalidPatternFailsBeforePermissionBroker()
    {
        WriteFile("a.cs", "needle");
        var broker = new FixedPermissionBroker(PermissionDecision.AllowOnce);
        var runtime = CreateRuntime(broker, CreateSearchTool());

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(
                SearchWorkspaceTextTool.Id,
                new SearchWorkspaceTextInput(
                    Workspace.Id,
                    "needle",
                    FilePattern: "src/*.cs")));

        Assert.Equal(ToolExecutionStatus.ValidationFailed, result.Status);
        Assert.Equal("invalid_file_pattern", result.SafeErrorCode);
        Assert.Equal(0, broker.RequestCount);
    }

    [Fact]
    public async Task SearchWorkspaceTextTool_FindsMatchBeyondReadCharacterLimit()
    {
        var limitedOptions = Options with { MaxReadCharacters = 8 };
        var reader = new FileSystemWorkspaceReader(Registry, Resolver, limitedOptions);
        WriteFile("late.txt", new string('a', limitedOptions.MaxReadCharacters + 25) + "needle");

        var output = await new SearchWorkspaceTextTool(reader, Resolver, limitedOptions).ExecuteAsync(
            new SearchWorkspaceTextInput(Workspace.Id, "needle"),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        var match = Assert.Single(output.Matches);
        Assert.Equal("late.txt", match.RelativeFilePath);
        Assert.False(output.IsTruncated);
    }

    [Fact]
    public async Task SearchWorkspaceTextTool_ExcludesGeneratedFoldersAndSkipsBinary()
    {
        WriteFile(Path.Combine(".git", "config"), "needle");
        WriteBytes("binary.bin", [0, 1, 2]);
        WriteFile("ok.txt", "needle");

        var output = await CreateSearchTool().ExecuteAsync(
            new SearchWorkspaceTextInput(Workspace.Id, "needle"),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        Assert.Single(output.Matches);
        Assert.True(output.FilesSkipped >= 1);
    }

    [Fact]
    public async Task SearchWorkspaceTextTool_ReparsePointFileIsSkippedWhenSupported()
    {
        var target = WriteFile("target.txt", "needle");
        var link = Path.Combine(Root, "link.txt");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch
        {
            return;
        }

        var output = await CreateSearchTool().ExecuteAsync(
            new SearchWorkspaceTextInput(Workspace.Id, "needle"),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        var match = Assert.Single(output.Matches);
        Assert.Equal("target.txt", match.RelativeFilePath);
    }

    [Fact]
    public async Task SearchWorkspaceTextTool_QueryLimitValidationFails()
    {
        var validation = await CreateSearchTool().ValidateAsync(
            new SearchWorkspaceTextInput(Workspace.Id, string.Empty));

        Assert.False(validation.IsValid);
        Assert.Equal("invalid_search_query", validation.SafeErrorCode);
    }

    [Fact]
    public async Task SearchWorkspaceTextTool_MaxCandidateFilesIsBounded()
    {
        var limitedOptions = Options with { MaxSearchFiles = 1 };
        var reader = new FileSystemWorkspaceReader(Registry, Resolver, limitedOptions);
        WriteFile("a.md", "needle");
        WriteFile("b.md", "needle");
        WriteFile("c.md", "needle");

        var output = await new SearchWorkspaceTextTool(reader, Resolver, limitedOptions).ExecuteAsync(
            new SearchWorkspaceTextInput(Workspace.Id, "needle", MaxResults: 10),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        Assert.True(output.IsTruncated);
        Assert.True(output.FilesScanned <= limitedOptions.MaxSearchFiles);
    }

    [Fact]
    public async Task SearchWorkspaceTextTool_MaxOpenedFilesIsBounded()
    {
        var limitedOptions = Options with { MaxSearchFiles = 2 };
        var reader = new FileSystemWorkspaceReader(Registry, Resolver, limitedOptions);
        WriteFile("a.txt", "missing");
        WriteFile("b.txt", "missing");
        WriteFile("c.txt", "missing");

        var output = await new SearchWorkspaceTextTool(reader, Resolver, limitedOptions).ExecuteAsync(
            new SearchWorkspaceTextInput(Workspace.Id, "needle", MaxResults: 10),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        Assert.True(output.IsTruncated);
        Assert.True(output.FilesScanned <= limitedOptions.MaxSearchFiles);
    }

    [Fact]
    public async Task SearchWorkspaceTextTool_PreviewIncludesMatchAndIsBounded()
    {
        WriteFile("long.txt", new string('a', Options.MaxPreviewLength + 50) + "needle");

        var output = await CreateSearchTool().ExecuteAsync(
            new SearchWorkspaceTextInput(Workspace.Id, "needle"),
            new TaskExecutionContext(TaskId.NewId(), DateTimeOffset.UtcNow));

        var match = Assert.Single(output.Matches);
        Assert.True(match.LinePreview.Length <= Options.MaxPreviewLength);
        Assert.Contains("needle", match.LinePreview, StringComparison.Ordinal);
        Assert.Equal("needle", match.LinePreview.Substring(match.MatchStartIndex, match.MatchLength));
    }

    [Fact]
    public async Task Runtime_CancellationRemainsDistinctForSearchWorkspaceTool()
    {
        WriteFile("a.txt", "needle");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runtime = CreateRuntime(
            new FixedPermissionBroker(PermissionDecision.AllowOnce),
            CreateSearchTool());

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(
                SearchWorkspaceTextTool.Id,
                new SearchWorkspaceTextInput(Workspace.Id, "needle")),
            cancellation.Token);

        Assert.Equal(ToolExecutionStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Runtime_SearchEventsDoNotContainMatchTextOrPreview()
    {
        WriteFile("secret.txt", "api_key=supersecret");
        var eventBus = new TaskEventBus();
        var broker = new FixedPermissionBroker(PermissionDecision.AllowOnce);
        var registry = new TypedToolRegistry();
        registry.Register(CreateSearchTool());
        var runtime = new TypedToolRuntime(
            registry,
            eventBus,
            broker,
            new ToolRuntimeOptions(TimeSpan.FromSeconds(5), UsePerTaskPermissionCache: true));
        var taskId = TaskId.NewId();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventsTask = CollectEventsAsync(eventBus.SubscribeAsync(taskId, cancellation.Token), 4);

        _ = await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(
                SearchWorkspaceTextTool.Id,
                new SearchWorkspaceTextInput(Workspace.Id, "supersecret")));
        var events = await eventsTask;

        Assert.DoesNotContain(
            events,
            taskEvent => taskEvent.Summary.Contains("supersecret", StringComparison.Ordinal) ||
                taskEvent.SafeMetadata?.Values.Any(value =>
                    value.Contains("supersecret", StringComparison.Ordinal)) == true);
    }

    [Fact]
    public async Task Runtime_WorkspaceEventsDoNotContainFileContents()
    {
        WriteFile("secret.txt", "api_key=supersecret");
        var eventBus = new TaskEventBus();
        var broker = new FixedPermissionBroker(PermissionDecision.AllowOnce);
        var registry = new TypedToolRegistry();
        registry.Register(CreateReadTool());
        var runtime = new TypedToolRuntime(
            registry,
            eventBus,
            broker,
            new ToolRuntimeOptions(TimeSpan.FromSeconds(5), UsePerTaskPermissionCache: true));
        var taskId = TaskId.NewId();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventsTask = CollectEventsAsync(eventBus.SubscribeAsync(taskId, cancellation.Token), 4);

        _ = await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(
                ReadTextFileTool.Id,
                new ReadTextFileInput(Workspace.Id, "secret.txt")));
        var events = await eventsTask;

        Assert.DoesNotContain(
            events,
            taskEvent => taskEvent.Summary.Contains("supersecret", StringComparison.Ordinal) ||
                taskEvent.SafeMetadata?.Values.Any(value =>
                    value.Contains("supersecret", StringComparison.Ordinal)) == true);
    }

    [Fact]
    public async Task Runtime_CancellationRemainsDistinctForWorkspaceTool()
    {
        WriteFile("a.txt", "a");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runtime = CreateRuntime(
            new FixedPermissionBroker(PermissionDecision.AllowOnce),
            CreateReadTool());

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(
                ReadTextFileTool.Id,
                new ReadTextFileInput(Workspace.Id, "a.txt")),
            cancellation.Token);

        Assert.Equal(ToolExecutionStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Runtime_RequestsInvocationSpecificPermissions()
    {
        WriteFile("a.txt", "a");
        WriteFile("b.txt", "b");
        var broker = new FixedPermissionBroker(PermissionDecision.AllowForTask);
        var runtime = CreateRuntime(broker, CreateReadTool());
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(
                ReadTextFileTool.Id,
                new ReadTextFileInput(Workspace.Id, "a.txt")));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(
                ReadTextFileTool.Id,
                new ReadTextFileInput(Workspace.Id, "b.txt")));

        Assert.Equal(2, broker.RequestCount);
        Assert.NotEqual(broker.Requests[0].ResourceScope, broker.Requests[1].ResourceScope);
    }

    [Fact]
    public async Task Runtime_ReusesSameInvocationSpecificResourceScope()
    {
        WriteFile("a.txt", "a");
        var broker = new FixedPermissionBroker(PermissionDecision.AllowForTask);
        var runtime = CreateRuntime(broker, CreateReadTool());
        var taskId = TaskId.NewId();

        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(
                ReadTextFileTool.Id,
                new ReadTextFileInput(Workspace.Id, "a.txt")));
        await runtime.InvokeAsync(
            taskId,
            new ToolInvocation(
                ReadTextFileTool.Id,
                new ReadTextFileInput(Workspace.Id, @".\a.txt")));

        Assert.Equal(1, broker.RequestCount);
    }

    [Fact]
    public async Task Runtime_DenialPreventsExecution()
    {
        WriteFile("a.txt", "a");
        var broker = new FixedPermissionBroker(PermissionDecision.Deny);
        var runtime = CreateRuntime(broker, CreateReadTool());

        var result = await runtime.InvokeAsync(
            TaskId.NewId(),
            new ToolInvocation(
                ReadTextFileTool.Id,
                new ReadTextFileInput(Workspace.Id, "a.txt")));

        Assert.Equal(ToolExecutionStatus.PermissionDenied, result.Status);
    }

    private static async Task<IReadOnlyList<TaskEvent>> CollectEventsAsync(
        IAsyncEnumerable<TaskEvent> events,
        int count)
    {
        var received = new List<TaskEvent>();
        await foreach (var taskEvent in events)
        {
            received.Add(taskEvent);
            if (received.Count == count)
            {
                return received;
            }
        }

        return received;
    }
}
