using PersonalAI.Core.Context;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Ui;

public sealed class AssistFocusRestorationServiceTests
{
    [Fact]
    public void IsRestorationReady_WithNullForeground_ReturnsFalse()
    {
        var controller = new WindowsGuiFocusController(new FakeGuiFocusNative());
        var service = new AssistFocusRestorationService(controller);

        Assert.False(service.IsRestorationReady(null));
    }

    [Fact]
    public void IsRestorationReady_WhenReady_ReturnsTrue()
    {
        var native = new FakeGuiFocusNative();
        native.AddWindow(100, 42, 7, 0);
        native.AddWindow(110, 42, 7, 100);
        native.ForegroundWindow = 100;

        var controller = new WindowsGuiFocusController(native);
        var service = new AssistFocusRestorationService(controller);
        var target = Target();

        Assert.True(service.IsRestorationReady(target));
    }

    [Fact]
    public void IsRestorationReady_WhenNotReady_ReturnsFalse()
    {
        var native = new FakeGuiFocusNative();
        native.AddWindow(100, 42, 7, 0);
        native.ForegroundWindow = 0;

        var controller = new WindowsGuiFocusController(native);
        var service = new AssistFocusRestorationService(controller);
        var target = Target();

        Assert.False(service.IsRestorationReady(target));
    }

    [Fact]
    public void TryRestoreFocus_WithNullForeground_ReturnsFalse()
    {
        var controller = new WindowsGuiFocusController(new FakeGuiFocusNative());
        var service = new AssistFocusRestorationService(controller);

        Assert.False(service.TryRestoreFocus(null));
    }

    [Fact]
    public void TryRestoreFocus_WhenReady_RestoresAndReturnsTrue()
    {
        var native = new FakeGuiFocusNative();
        native.AddWindow(100, 42, 7, 0);
        native.AddWindow(110, 42, 7, 100);
        native.ForegroundWindow = 100;

        var controller = new WindowsGuiFocusController(native);
        var service = new AssistFocusRestorationService(controller);

        Assert.True(service.TryRestoreFocus(Target(), CancellationToken.None));
        Assert.Contains("foreground:100", native.Calls);
    }

    [Fact]
    public void TryRestoreFocus_WhenNotReady_ReturnsFalse()
    {
        var native = new FakeGuiFocusNative();
        native.AddWindow(100, 42, 7, 0);
        native.ForegroundWindow = 0;

        var controller = new WindowsGuiFocusController(native);
        var service = new AssistFocusRestorationService(controller);

        Assert.False(service.TryRestoreFocus(Target(), CancellationToken.None));
        Assert.Empty(native.Calls);
    }

    [Fact]
    public void TryRestoreFocus_NullForeground_DoesNotCallNative()
    {
        var native = new FakeGuiFocusNative();
        native.AddWindow(100, 42, 7, 0);
        native.ForegroundWindow = 100;

        var controller = new WindowsGuiFocusController(native);
        var service = new AssistFocusRestorationService(controller);

        Assert.False(service.TryRestoreFocus(null, CancellationToken.None));
        Assert.Empty(native.Calls);
    }

    [Fact]
    public void IsRestorationReady_WithNoRestoreRequest_ReturnsFalse()
    {
        // Simulates a pending request with ShouldRestore == false (e.g. AppOpen/ModuleOpen):
        // the service should report not-ready so the window's RestoreFocus skips restoration.
        var native = new FakeGuiFocusNative();
        native.AddWindow(100, 42, 7, 0);
        native.ForegroundWindow = 100;

        var controller = new WindowsGuiFocusController(native);
        var service = new AssistFocusRestorationService(controller);

        // No-restore requests should not reach the service at all, but even if they
        // do, the service must not restore.  Passing null (no foreground) achieves this.
        Assert.False(service.IsRestorationReady(null));
        Assert.False(service.TryRestoreFocus(null, CancellationToken.None));
        Assert.Empty(native.Calls);
    }

    private static ActiveWindowReference Target() => new(
        100,
        42,
        "browser",
        "mail",
        DateTimeOffset.UtcNow,
        new GuiThreadWindowSnapshot(
            7,
            42,
            100,
            110,
            0,
            0,
            0,
            0,
            DateTimeOffset.UtcNow));

    private sealed class FakeGuiFocusNative : IGuiFocusNative
    {
        private readonly Dictionary<nint, (uint Process, uint Thread, nint Parent)> _windows = [];

        public nint ForegroundWindow { get; set; }
        public uint CurrentThreadId => 1;
        public nint CurrentFocus { get; set; }
        public List<string> Calls { get; } = [];

        public void AddWindow(nint window, uint process, uint thread, nint parent) =>
            _windows[window] = (process, thread, parent);

        public bool IsWindow(nint window) => _windows.ContainsKey(window);

        public uint GetWindowThread(nint window, out uint processId)
        {
            if (_windows.TryGetValue(window, out var info))
            {
                processId = info.Process;
                return info.Thread;
            }

            processId = 0;
            return 0;
        }

        public bool IsChild(nint parent, nint child)
        {
            while (_windows.TryGetValue(child, out var info) && info.Parent != 0)
            {
                if (info.Parent == parent)
                {
                    return true;
                }

                child = info.Parent;
            }

            return false;
        }

        public GuiThreadWindowSnapshot? GetGuiThread(uint threadId, uint processId)
        {
            nint focused = 0;
            foreach (var (window, info) in _windows)
            {
                if (info.Process == processId && info.Thread == threadId &&
                    window != 100)
                {
                    focused = window;
                    break;
                }
            }

            return new GuiThreadWindowSnapshot(
                threadId, processId, 100, focused, 0, 0, 0, 0, DateTimeOffset.UtcNow);
        }

        public bool SetForegroundWindow(nint window)
        {
            Calls.Add($"foreground:{window}");
            ForegroundWindow = window;
            return true;
        }

        public bool BringWindowToTop(nint window)
        {
            Calls.Add($"bring:{window}");
            return true;
        }

        public bool AttachThreadInput(uint sourceThread, uint targetThread, bool attach)
        {
            Calls.Add($"attach:{sourceThread}:{targetThread}:{attach}");
            return true;
        }

        public nint SetActiveWindow(nint window)
        {
            Calls.Add($"active:{window}");
            return window;
        }

        public nint SetFocus(nint window)
        {
            Calls.Add($"focus:{window}");
            CurrentFocus = window;
            return window;
        }
    }
}
