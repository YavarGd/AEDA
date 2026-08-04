using System.Runtime.CompilerServices;
using PersonalAI.Core.Ui;
using PersonalAI.Desktop.Avalonia.Platform.Windows;

namespace PersonalAI.Tests.Avalonia.Windows;

public sealed class AvaloniaPalettePlacementAndWindowsShellAdapterTests
{
    [Fact]
    public void PlacementPreservesNegativeCoordinateMonitor()
    {
        var position = AvaloniaWindowPlacementService.ResolvePosition(
            new WindowPosition(-1500, 120),
            500,
            300,
            [new RectBounds(-1920, 0, 1920, 1040), new RectBounds(0, 0, 1920, 1040)],
            new RectBounds(0, 0, 1920, 1040));

        Assert.Equal(new WindowPosition(-1500, 120), position);
    }

    [Fact]
    public void DisconnectedMonitorPositionFallsBackToPrimary()
    {
        var position = AvaloniaWindowPlacementService.ResolvePosition(
            new WindowPosition(3000, 200),
            500,
            300,
            [new RectBounds(0, 0, 1920, 1040)],
            new RectBounds(0, 0, 1920, 1040));

        Assert.Equal(new WindowPosition(710, 370), position);
    }

    [Fact]
    public async Task MissingClipboardFailsWithoutIncludingClipboardText()
    {
        const string privateText = "private clipboard text";
        var writer = new AvaloniaClipboardWriter(() => null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.CopyTextAsync(privateText));

        Assert.DoesNotContain(privateText, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AdaptersUseAvaloniaNativeShellApis()
    {
        var tray = ReadSource("AvaloniaTrayIconService.cs");
        var placement = ReadSource("AvaloniaWindowPlacementService.cs");
        var identity = ReadSource("AvaloniaWindowsProcessIdentity.cs");
        var activation = ReadSource("AvaloniaWindowActivationService.cs");
        var clipboard = ReadSource("AvaloniaClipboardWriter.cs");

        Assert.Contains("new TrayIcon", tray);
        Assert.Contains("new NativeMenu", tray);
        Assert.DoesNotContain("Shell_NotifyIcon", tray);
        Assert.Contains("window.Screens", placement);
        Assert.Contains("TryGetPlatformHandle", identity);
        Assert.Contains("GetWindowThreadProcessId", identity);
        Assert.Contains("window.Activate()", activation);
        Assert.Contains("ShowWindow", activation);
        Assert.Contains("SetForegroundWindow", activation);
        Assert.Contains("SetTextAsync", clipboard);
        Assert.Contains("FlushAsync", clipboard);
    }

    private static string ReadSource(
        string file,
        [CallerFilePath] string testFilePath = "")
    {
        var directory = Path.GetDirectoryName(testFilePath)!;
        var root = Path.GetFullPath(Path.Combine(directory, "..", "..", ".."));
        var path = Path.Combine(
            root,
            "PersonalAI.Desktop.Avalonia",
            "Platform",
            "Windows",
            file);
        Assert.True(File.Exists(path), $"Avalonia Windows adapter not found at {path}");
        return File.ReadAllText(path);
    }
}
