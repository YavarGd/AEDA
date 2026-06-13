using System.Reflection;
using PersonalAI.Core.Tasks;

namespace PersonalAI.Tests.Tasks;

public sealed class TaskEventTests
{
    [Fact]
    public void TaskEvent_DoesNotExposePublicRawConstructor()
    {
        var constructors = typeof(TaskEvent).GetConstructors(
            BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(constructors);
    }

    [Fact]
    public void Create_SanitizesSummary()
    {
        var taskEvent = TaskEvent.Create(
            TaskId.NewId(),
            TaskEventKind.ToolStarted,
            "token=abc123");

        Assert.Equal("[redacted]", taskEvent.Summary);
    }

    [Fact]
    public void Create_CopiesMetadataInsteadOfReferencingCallerDictionary()
    {
        var metadata = new Dictionary<string, string>
        {
            ["phase"] = "before"
        };

        var taskEvent = TaskEvent.Create(
            TaskId.NewId(),
            TaskEventKind.ToolStarted,
            "started",
            safeMetadata: metadata);

        metadata["phase"] = "after";

        Assert.Equal("before", taskEvent.SafeMetadata!["phase"]);
        Assert.Throws<NotSupportedException>(
            () => ((IDictionary<string, string>)taskEvent.SafeMetadata)["phase"] = "mutated");
    }

    [Fact]
    public void Create_EnforcesMetadataKeyLimits()
    {
        Assert.Throws<ArgumentException>(() => TaskEvent.Create(
            TaskId.NewId(),
            TaskEventKind.ToolStarted,
            "started",
            safeMetadata: new Dictionary<string, string>
            {
                [new string('k', TaskEventMetadata.MaxMetadataKeyLength + 1)] = "value"
            }));
    }

    [Fact]
    public void Create_EnforcesMetadataValueLimits()
    {
        var taskEvent = TaskEvent.Create(
            TaskId.NewId(),
            TaskEventKind.ToolStarted,
            "started",
            safeMetadata: new Dictionary<string, string>
            {
                ["phase"] = new string('v', TaskEventMetadata.MaxMetadataValueLength + 10)
            });

        Assert.Equal(
            TaskEventMetadata.MaxMetadataValueLength,
            taskEvent.SafeMetadata!["phase"].Length);
    }

    [Fact]
    public void Create_RedactsSecretLikeMetadata()
    {
        var taskEvent = TaskEvent.Create(
            TaskId.NewId(),
            TaskEventKind.ToolStarted,
            "started",
            safeMetadata: new Dictionary<string, string>
            {
                ["phase"] = "access_token=abc"
            });

        Assert.Equal("[redacted]", taskEvent.SafeMetadata!["phase"]);
    }

    [Fact]
    public void Create_SanitizesAndBoundsProgressLabel()
    {
        var taskEvent = TaskEvent.Create(
            TaskId.NewId(),
            TaskEventKind.TaskStatusChanged,
            "running",
            progressLabel: $"line1{Environment.NewLine}token=abc{new string('x', 200)}");

        Assert.Equal("[redacted]", taskEvent.ProgressLabel);
        Assert.DoesNotContain(Environment.NewLine, taskEvent.ProgressLabel, StringComparison.Ordinal);
        Assert.True(taskEvent.ProgressLabel!.Length <= TaskEventMetadata.MaxProgressLabelLength);
    }

    [Fact]
    public void Create_SanitizesAndBoundsSafeErrorMessage()
    {
        var taskEvent = TaskEvent.Create(
            TaskId.NewId(),
            TaskEventKind.ToolFailed,
            "failed",
            safeErrorMessage: $"System.Exception: token=abc{Environment.NewLine} at path");

        Assert.Equal("[redacted]", taskEvent.SafeErrorMessage);
    }

    [Theory]
    [InlineData("tool_timeout")]
    [InlineData("permission_denied")]
    [InlineData("validation.failed")]
    [InlineData("tool-failed")]
    public void Create_PreservesValidErrorCodes(string errorCode)
    {
        var taskEvent = TaskEvent.Create(
            TaskId.NewId(),
            TaskEventKind.ToolFailed,
            "failed",
            safeErrorCode: errorCode);

        Assert.Equal(errorCode, taskEvent.SafeErrorCode);
    }

    [Theory]
    [InlineData("Bearer abc123")]
    [InlineData(@"C:\Users\name\secret.txt")]
    [InlineData("System.Exception: failed")]
    [InlineData("token=abc")]
    [InlineData("UPPERCASE")]
    public void Create_RejectsInvalidErrorCodes(string errorCode)
    {
        Assert.Throws<ArgumentException>(() => TaskEvent.Create(
            TaskId.NewId(),
            TaskEventKind.ToolFailed,
            "failed",
            safeErrorCode: errorCode));
    }

    [Fact]
    public void Create_DoesNotAllowRawExceptionTextOrStackTraceThroughSupportedApi()
    {
        var taskEvent = TaskEvent.Create(
            TaskId.NewId(),
            TaskEventKind.ToolFailed,
            "System.Exception: token=abc",
            safeErrorMessage: $"System.Exception: token=abc{Environment.NewLine} at Secret.Frame()");

        Assert.DoesNotContain("token=abc", taskEvent.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret.Frame", taskEvent.SafeErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", taskEvent.SafeErrorMessage, StringComparison.Ordinal);
    }
}
