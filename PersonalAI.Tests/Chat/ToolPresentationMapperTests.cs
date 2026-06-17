using System.Text.Json;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;
using PersonalAI.Desktop.WinUI.Models;
using PersonalAI.Desktop.WinUI.ViewModels;

namespace PersonalAI.Tests.Chat;

public sealed class ToolPresentationMapperTests
{
    [Theory]
    [InlineData("workspace.directory.list", "List files")]
    [InlineData("workspace.file.read_text", "Read file")]
    [InlineData("workspace.text.search", "Search text")]
    [InlineData("workspace.info.get", "View workspace information")]
    [InlineData("unknown.tool", "Use workspace tool")]
    public void FriendlyAction_MapsToolNames(string toolId, string expected)
    {
        Assert.Equal(expected, ToolPresentationMapper.FriendlyAction(new ToolId(toolId)));
    }

    [Fact]
    public void ForPermission_GeneratesClearWorkspaceAccessCopy()
    {
        var request = new PermissionRequest(
            Guid.NewGuid(),
            TaskId.NewId(),
            new ToolId("workspace.file.read_text"),
            "Read text file",
            [ToolPermission.ReadWorkspace],
            PermissionRiskLevel.Low,
            "Read file 'README.md' in workspace 'Tool UX'.",
            "WORKSPACE:ABC:README.MD",
            PermissionAccessMode.Read,
            IsReadOnly: true);

        var presentation = ToolPresentationMapper.ForPermission(request);

        Assert.Equal("Allow workspace access?", presentation.Title);
        Assert.Equal("Read file", presentation.Action);
        Assert.Contains("README.md", presentation.Explanation, StringComparison.Ordinal);
        Assert.Equal("Workspace path: README.MD", presentation.Scope);
        Assert.Contains("read-only access", presentation.ReadOnlyExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot modify files", presentation.ReadOnlyExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequestedActivity_HidesAbsolutePathsByDefault()
    {
        var call = CreateCall(
            "workspace.file.read_text",
            new
            {
                workspaceId = "workspace-a",
                relativePath = @"C:\Users\name\secret.txt"
            });

        var activity = ToolPresentationMapper.RequestedActivity(call, workspaceRegistry: null);

        Assert.Contains("Requested path", activity, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users", activity, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(TaskEventKind.PermissionRequested, "Waiting for permission", false)]
    [InlineData(TaskEventKind.PermissionGranted, "Permission approved", false)]
    [InlineData(TaskEventKind.PermissionDenied, "Permission denied", false)]
    [InlineData(TaskEventKind.ToolCancelled, "Cancelled", false)]
    [InlineData(TaskEventKind.ToolCompleted, "Completed", false)]
    [InlineData(TaskEventKind.ToolFailed, "Failed", true)]
    public void ForTaskEvent_MapsActivityStates(
        TaskEventKind kind,
        string expectedState,
        bool expectedProblem)
    {
        var taskEvent = TaskEvent.Create(
            TaskId.NewId(),
            kind,
            "Tool event.",
            toolId: new ToolId("workspace.text.search"));

        var presentation = ToolPresentationMapper.ForTaskEvent(taskEvent);

        Assert.Equal(expectedState, presentation.State);
        Assert.Equal(expectedProblem, presentation.IsProblem);
        Assert.DoesNotContain("workspace.text.search", presentation.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletedActivity_ShowsTruncatedAndMatchCount()
    {
        var call = CreateCall(
            "workspace.text.search",
            new
            {
                workspaceId = "workspace-a",
                query = "WorkspaceToolUxMarker",
                relativeDirectory = "src"
            });
        using var output = JsonDocument.Parse(
            """
            {"matches":[{"relativeFilePath":"src/ExampleService.cs"}],"isTruncated":true}
            """);
        var result = new ChatToolResultPayload(
            "call-1",
            "workspace.text.search",
            true,
            ToolExecutionStatus.Succeeded.ToString(),
            "Search completed.",
            null,
            null,
            output.RootElement.Clone(),
            true);

        var activity = ToolPresentationMapper.CompletedActivity(
            call,
            result,
            workspaceRegistry: null);

        Assert.Contains("Result truncated", activity, StringComparison.Ordinal);
        Assert.Contains("1 matches", activity, StringComparison.Ordinal);
        Assert.Contains("WorkspaceToolUxMarker", activity, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatStoredToolActivity_ReloadsFriendlyToolResult()
    {
        var content =
            """
            {"toolCallId":"call-1","toolName":"workspace.directory.list","isSuccess":true,"status":"Succeeded","summary":"Listed directory.","safeErrorCode":null,"safeErrorMessage":null,"output":{"entries":[]},"isTruncated":false}
            """;

        var activity = ToolPresentationMapper.FormatStoredToolActivity(content);

        Assert.Contains("List files", activity, StringComparison.Ordinal);
        Assert.DoesNotContain("workspace.directory.list", activity, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatMessageViewModel_ToolActivityIsDistinctFromAssistantMessage()
    {
        var tool = new ChatMessageViewModel(ChatRole.Tool, "List files completed.");
        var assistant = new ChatMessageViewModel(ChatRole.Assistant, "Done.");

        Assert.True(tool.IsToolActivity);
        Assert.Equal("Tool activity", tool.RoleLabel);
        Assert.False(assistant.IsToolActivity);
        Assert.Equal("Assistant", assistant.RoleLabel);
    }

    private static ChatToolCall CreateCall(string toolName, object arguments)
    {
        var element = JsonSerializer.SerializeToElement(
            arguments,
            ChatToolJson.SerializerOptions);
        return new ChatToolCall("call-1", toolName, element);
    }
}
