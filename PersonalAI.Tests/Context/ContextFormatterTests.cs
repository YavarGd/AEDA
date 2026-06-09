using PersonalAI.Core.Context;

namespace PersonalAI.Tests.Context;

public sealed class ContextFormatterTests
{
    [Fact]
    public void FormatPreview_HandlesMissingProcessInformation()
    {
        var context = new ActiveApplicationContext(
            WindowHandle: null,
            ProcessId: null,
            ProcessName: null,
            ExecutablePath: null,
            WindowTitle: "Untitled - Notepad",
            CapturedSelectedText: null,
            ScreenshotPath: null,
            ScreenshotBytes: null,
            CapturedAtUtc: DateTimeOffset.UtcNow);

        var preview = ContextFormatter.FormatPreview(context);

        Assert.Contains("Window: Untitled - Notepad", preview);
        Assert.DoesNotContain("Application:", preview);
        Assert.DoesNotContain("Executable:", preview);
    }

    [Fact]
    public void FormatPreview_HandlesMissingExecutablePathAndEmptyWindowTitle()
    {
        var context = new ActiveApplicationContext(
            WindowHandle: 42,
            ProcessId: 1000,
            ProcessName: "pwsh",
            ExecutablePath: null,
            WindowTitle: "",
            CapturedSelectedText: null,
            ScreenshotPath: null,
            ScreenshotBytes: null,
            CapturedAtUtc: DateTimeOffset.UtcNow);

        var preview = ContextFormatter.FormatPreview(context);

        Assert.Contains("Application: pwsh", preview);
        Assert.DoesNotContain("Executable:", preview);
        Assert.DoesNotContain("Window:", preview);
    }

    [Fact]
    public void FormatPromptBlock_IncludesSeparatedTextualContext()
    {
        var context = new ActiveApplicationContext(
            WindowHandle: 123,
            ProcessId: 456,
            ProcessName: "notepad",
            ExecutablePath: @"C:\Windows\System32\notepad.exe",
            WindowTitle: "notes.txt",
            CapturedSelectedText: "selected words",
            ScreenshotPath: @"C:\Temp\screenshot.png",
            ScreenshotBytes: null,
            CapturedAtUtc: DateTimeOffset.UtcNow);

        var block = ContextFormatter.FormatPromptBlock(context);

        Assert.Contains("Attached active-window context", block);
        Assert.Contains("---", block);
        Assert.Contains("Application: notepad", block);
        Assert.Contains("Selected or clipboard text:", block);
        Assert.Contains("selected words", block);
        Assert.DoesNotContain("screenshot.png", block);
    }
}
