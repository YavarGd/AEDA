using System.Runtime.CompilerServices;
using PersonalAI.Infrastructure.Windows;

namespace PersonalAI.Tests.Avalonia.Lifecycle;

public sealed class AvaloniaEditorAndBackgroundLifecycleTests
{
    [Fact]
    public void SingleInstanceAcquisitionHasOneControlledOwner()
    {
        var mutexName = $"Local\\AEDA.Tests.{Guid.NewGuid():N}";

        using var first = new WindowsSingleInstanceService(mutexName);
        using var second = new WindowsSingleInstanceService(mutexName);

        Assert.True(first.IsPrimaryInstance);
        Assert.False(second.IsPrimaryInstance);
    }

    [Fact]
    public void SecondaryHandoffPrecedesAvaloniaAndUnexpectedFailuresAreNotSwallowed()
    {
        var app = ReadSource("App.axaml.cs");
        var program = ReadSource("Program.cs");
        var acquire = program.IndexOf("new WindowsSingleInstanceService()", StringComparison.Ordinal);
        var secondary = program.IndexOf("if (!singleInstance.IsPrimaryInstance)", StringComparison.Ordinal);
        var handoff = program.IndexOf(
            "PersonalAiActivationClient.TryActivatePrimaryAsync()",
            secondary,
            StringComparison.Ordinal);
        var initializeAvalonia = program.IndexOf("BuildAvaloniaApp()", handoff, StringComparison.Ordinal);

        Assert.True(acquire >= 0 && acquire < secondary);
        Assert.True(secondary < handoff && handoff < initializeAvalonia);
        Assert.Contains("? 0", program);
        Assert.Contains(": 1", program);
        Assert.DoesNotContain("catch", program);
        Assert.DoesNotContain("WindowsSingleInstanceService", app);
    }

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
        Assert.Contains("pipeServer?.DisposeAsync()", app);
        Assert.Contains("_hotKey?.Dispose()", app);
        Assert.Contains("_trayIcon?.Dispose()", app);
        Assert.Contains("composition?.DisposeAsync()", app);
        Assert.Contains("new AvaloniaShutdownCoordinator(", app);
        Assert.Contains("desktop.Shutdown", app);
        Assert.DoesNotContain("GetAwaiter().GetResult()", app);
        Assert.DoesNotContain("desktop.Exit +=", app);
    }

    [Fact]
    public void ExistingActivationCallbackRestoresVisibleHiddenAndStartMinimizedPrimary()
    {
        var app = ReadSource("App.axaml.cs");
        var activation = ReadSource("Platform", "Windows", "AvaloniaWindowActivationService.cs");
        var createActivation = app.IndexOf(
            "_mainWindowActivation = new AvaloniaWindowActivationService(window);",
            StringComparison.Ordinal);
        var startPipe = app.IndexOf("StartEditorIpc(_composition);", StringComparison.Ordinal);

        Assert.True(createActivation >= 0 && createActivation < startPipe);
        Assert.Contains("() => Dispatcher.UIThread.Post(ShowMainWindow)", app);
        Assert.Contains("if (!_composition.StartMinimizedToTray)", app);
        Assert.Contains("if (!window.IsVisible)", activation);
        Assert.Contains("window.WindowState == WindowState.Minimized", activation);
        Assert.Contains("window.WindowState = WindowState.Normal", activation);
        Assert.Contains("window.Activate()", activation);
    }

    [Fact]
    public void TrayAndWindowExitUseTheSameAsyncShutdownPathWhileCloseToTrayStaysDistinct()
    {
        var app = ReadSource("App.axaml.cs");

        Assert.Contains("new AvaloniaTrayIconService(\n            ShowMainWindow,\n            NewChat,\n            () => RequestExit())", app.Replace("\r\n", "\n"));
        Assert.Contains("if (_composition?.ExitOnMainWindowClose == true)", app);
        Assert.Contains("RequestExit();", app);
        Assert.Contains("else\n        {\n            HideMainWindow();\n        }", app.Replace("\r\n", "\n"));
        Assert.Contains("_shutdownCoordinator?.BeginAsync(exitCode)", app);
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
