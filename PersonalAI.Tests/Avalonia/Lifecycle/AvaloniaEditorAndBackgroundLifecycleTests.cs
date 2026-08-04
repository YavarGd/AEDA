using System.Runtime.CompilerServices;

namespace PersonalAI.Tests.Avalonia.Lifecycle;

public sealed class AvaloniaEditorAndBackgroundLifecycleTests
{
    [Fact]
    public void AppOwnsOneBackgroundShellAndDisposesNativeResources()
    {
        var app = ReadSource("App.axaml.cs");
        var program = ReadSource("Program.cs");
        var composition = ReadSource("Composition", "AvaloniaAppComposition.cs");

        Assert.Equal(1, Count(app, "new MainWindow()"));
        Assert.Equal(1, Count(app, "composition.CreateAssistWindow()"));
        Assert.Contains("ShutdownMode.OnExplicitShutdown", program);
        Assert.Contains("new AvaloniaTrayIconService(", app);
        Assert.Contains("new WindowsGlobalHotKeyService(", app);
        Assert.Contains("new PersonalAiPipeServer(handler)", app);
        Assert.Contains("StartMinimizedToTray", app);
        Assert.Contains("ExitOnMainWindowClose", app);
        Assert.Contains("AskBeforeMainWindowExit", app);
        Assert.Contains("CloseBehavior.Exit", composition);
        Assert.Contains("CloseBehavior.AskEachTime", composition);
        Assert.Contains("_pipeServer?.Dispose()", app);
        Assert.Contains("_hotKey?.Dispose()", app);
        Assert.Contains("_trayIcon?.Dispose()", app);
        Assert.Contains("_composition.DisposeAsync()", app);
    }

    [Fact]
    public void PassiveEditorContextDoesNotOpenOrAttachToAvalonia()
    {
        var app = ReadSource("App.axaml.cs");
        var passive = app.IndexOf("EditorContextCommands.UpdateSelectionContext", StringComparison.Ordinal);
        var earlyReturn = app.IndexOf("return;", passive, StringComparison.Ordinal);
        var show = app.IndexOf("ShowMainWindow();", passive, StringComparison.Ordinal);

        Assert.True(passive >= 0 && earlyReturn > passive && show > earlyReturn);
        Assert.DoesNotContain("ReceiveEditorContext", app);
    }

    private static int Count(string value, string text) =>
        value.Split(text, StringSplitOptions.None).Length - 1;

    private static string ReadSource(
        params string[] parts)
    {
        var testFilePath = GetTestFilePath();
        var directory = Path.GetDirectoryName(testFilePath)!;
        var root = Path.GetFullPath(Path.Combine(directory, "..", "..", ".."));
        return File.ReadAllText(Path.Combine([root, "PersonalAI.Desktop.Avalonia", .. parts]));
    }

    private static string GetTestFilePath(
        [CallerFilePath] string testFilePath = "")
        => testFilePath;
}
