using PersonalAI.Core.Context;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Ui;

public sealed class WindowsGuiFocusControllerTests
{
    [Theory]
    [InlineData("Edit", 0x20, true)]
    [InlineData("RichEditD2DPT", 0x20, true)]
    [InlineData("Edit", 0, false)]
    [InlineData("Scintilla", 0x20, false)]
    [InlineData("Chrome_RenderWidgetHostHWND", 0x20, false)]
    public void PasswordStyleOnlyBlocksNativeEditClasses(
        string className,
        long style,
        bool expected) =>
        Assert.Equal(
            expected,
            NativePasswordControlPolicy.IsPasswordEdit(className, style));

    [Fact]
    public void RestoreUsesTopLevelThenChildAndDetachesInputQueues()
    {
        var native = Native();
        var controller = new WindowsGuiFocusController(native);

        Assert.True(controller.TryRestore(Target(), CancellationToken.None));

        Assert.Equal(
            ["foreground:100", "bring:100", "attach:1:7:True", "active:100", "focus:110", "attach:1:7:False"],
            native.Calls);
        Assert.Equal(GuiFocusRestoreState.Ready, controller.GetState(Target()));
    }

    [Fact]
    public void CancellationAfterAttachStillDetaches()
    {
        using var cancellation = new CancellationTokenSource();
        var native = Native();
        native.OnSetActive = cancellation.Cancel;
        var controller = new WindowsGuiFocusController(native);

        Assert.Throws<OperationCanceledException>(() =>
            controller.TryRestore(Target(), cancellation.Token));

        Assert.Equal("attach:1:7:False", native.Calls[^1]);
        Assert.DoesNotContain("focus:110", native.Calls);
    }

    [Fact]
    public void FailureAfterAttachStillDetaches()
    {
        var native = Native();
        native.ThrowOnSetFocus = true;
        var controller = new WindowsGuiFocusController(native);

        Assert.Throws<InvalidOperationException>(() =>
            controller.TryRestore(Target(), CancellationToken.None));

        Assert.Equal("attach:1:7:False", native.Calls[^1]);
    }

    [Fact]
    public void UnrelatedFocusedControlIsRejected()
    {
        var native = Native();
        native.ForegroundWindow = 100;
        native.AddWindow(120, 42, 7, 100);
        native.CurrentFocus = 120;

        Assert.Equal(
            GuiFocusRestoreState.ChildNotFocused,
            new WindowsGuiFocusController(native).GetState(Target()));
    }

    [Fact]
    public void DescendantOfRememberedRendererIsAccepted()
    {
        var native = Native();
        native.ForegroundWindow = 100;
        native.AddWindow(111, 42, 7, 110);
        native.CurrentFocus = 111;

        Assert.Equal(
            GuiFocusRestoreState.Ready,
            new WindowsGuiFocusController(native).GetState(Target()));
    }

    [Fact]
    public void CaretWindowIsUsedWhenFocusedWindowWasUnavailable()
    {
        var native = Native();
        native.AddWindow(120, 42, 7, 100);
        var target = Target() with
        {
            GuiThread = Snapshot(focused: 0, caret: 120)
        };

        Assert.True(new WindowsGuiFocusController(native)
            .TryRestore(target, CancellationToken.None));
        Assert.Contains("focus:120", native.Calls);
    }

    [Theory]
    [InlineData(false, 42)]
    [InlineData(true, 99)]
    public void DestroyedOrReusedFocusedChildIsRejected(
        bool childExists,
        uint childProcess)
    {
        var native = Native(includeFocusedChild: false);
        if (childExists)
        {
            native.AddWindow(110, childProcess, 7, 100);
        }

        Assert.Equal(
            GuiFocusRestoreState.TargetInvalid,
            new WindowsGuiFocusController(native).GetState(Target()));
    }

    [Fact]
    public void StaleTopLevelProcessOrThreadIsRejected()
    {
        var native = Native(includeFocusedChild: false);
        native.AddWindow(100, 99, 8, 0);

        Assert.Equal(
            GuiFocusRestoreState.TargetInvalid,
            new WindowsGuiFocusController(native).GetState(Target()));
    }

    private static ActiveWindowReference Target() => new(
        100,
        42,
        "browser",
        "mail",
        DateTimeOffset.UtcNow,
        Snapshot());

    private static GuiThreadWindowSnapshot Snapshot(
        nint focused = 110,
        nint caret = 0) => new(
        7,
        42,
        100,
        focused,
        0,
        0,
        0,
        caret,
        DateTimeOffset.UtcNow);

    private static FakeGuiFocusNative Native(bool includeFocusedChild = true)
    {
        var native = new FakeGuiFocusNative();
        native.AddWindow(100, 42, 7, 0);
        if (includeFocusedChild)
        {
            native.AddWindow(110, 42, 7, 100);
        }

        return native;
    }

    private sealed class FakeGuiFocusNative : IGuiFocusNative
    {
        private readonly Dictionary<nint, WindowInfo> _windows = [];

        public nint ForegroundWindow { get; set; }
        public uint CurrentThreadId => 1;
        public nint CurrentFocus { get; set; }
        public List<string> Calls { get; } = [];
        public Action? OnSetActive { get; set; }
        public bool ThrowOnSetFocus { get; set; }

        public void AddWindow(nint window, uint process, uint thread, nint parent) =>
            _windows[window] = new(process, thread, parent);

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

        public GuiThreadWindowSnapshot? GetGuiThread(uint threadId, uint processId) =>
            Snapshot(CurrentFocus);

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
            OnSetActive?.Invoke();
            return window;
        }

        public nint SetFocus(nint window)
        {
            Calls.Add($"focus:{window}");
            if (ThrowOnSetFocus)
            {
                throw new InvalidOperationException();
            }

            CurrentFocus = window;
            return window;
        }

        private sealed record WindowInfo(uint Process, uint Thread, nint Parent);
    }
}
