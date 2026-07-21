using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Ui;

public sealed class ClipboardCopySelectedTextProviderTests
{
    private static readonly ActiveWindowReference Foreground = new(
        1, 42, "notepad", "notes.txt - Notepad", DateTimeOffset.UtcNow);

    [Fact]
    public void NativeInputLayoutMatchesWin32Abi()
    {
        var native = typeof(WindowsClipboardCopySelectedTextProvider).GetNestedType(
            "Native",
            System.Reflection.BindingFlags.NonPublic)!;
        var input = native.GetNestedType(
            "Input",
            System.Reflection.BindingFlags.Public)!;

        Assert.Equal(
            IntPtr.Size == 8 ? 40 : 28,
            System.Runtime.InteropServices.Marshal.SizeOf(input));
    }

    [Fact]
    public async Task CopyChangeReturnsTextAndRestoresSnapshot()
    {
        var clipboard = new FakeClipboard("before", "selected");
        var input = new FakeInput(() => clipboard.Sequence++)
        {
            ForegroundChecksBeforeSuccess = 1
        };
        var provider = Provider(clipboard, input);

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("selected", result.Text);
        Assert.True(clipboard.RestoreCalled);
        Assert.True(input.FocusRestored);
        Assert.True(input.TargetFocusRequested);
        Assert.Equal(1, input.CopyCalls);
    }

    [Fact]
    public async Task NoSequenceChangeRejectsOldClipboardText()
    {
        var clipboard = new FakeClipboard("old", "old");
        var provider = Provider(clipboard, new FakeInput());

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(SelectedTextCaptureFailure.ClipboardDidNotChange, result.FailureReason);
        Assert.False(clipboard.RestoreCalled);
    }

    [Fact]
    public async Task UnrelatedSecondChangePreventsOverwriteAndSurfacesFailure()
    {
        var clipboard = new FakeClipboard("before", "selected") { RaceBeforeRestore = true };
        var provider = Provider(clipboard, new FakeInput(() => clipboard.Sequence++));

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(SelectedTextCaptureFailure.ClipboardRestoreFailed, result.FailureReason);
        Assert.False(result.ClipboardRestorationSucceeded);
        Assert.True(clipboard.RestoreCalled);
    }

    [Fact]
    public async Task EmptyCopiedTextStillRestoresMultiFormatSnapshot()
    {
        var formats = new Dictionary<string, byte[]>
        {
            ["UnicodeText"] = [1, 2],
            ["HTML Format"] = [3, 4],
            ["Rich Text Format"] = [5, 6],
            ["FileDrop"] = [7, 8],
            ["DIB"] = [9, 10]
        };
        var clipboard = new FakeClipboard("before", " ") { SnapshotState = formats };
        var provider = Provider(clipboard, new FakeInput(() => clipboard.Sequence++));

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.Equal(SelectedTextCaptureFailure.NoSelection, result.FailureReason);
        Assert.True(clipboard.RestoreCalled);
        Assert.Same(formats, clipboard.RestoredState);
    }

    [Fact]
    public async Task CancellationAfterCopyStillRestoresClipboardAndFocus()
    {
        using var cancellation = new CancellationTokenSource();
        var clipboard = new FakeClipboard("before", "selected");
        var input = new FakeInput(() =>
        {
            clipboard.Sequence++;
            cancellation.Cancel();
        });
        var provider = Provider(clipboard, input);

        var result = await provider.CaptureAsync(Request(), cancellation.Token);

        Assert.Equal(SelectedTextCaptureFailure.Cancelled, result.FailureReason);
        Assert.True(clipboard.RestoreCalled);
        Assert.True(input.FocusRestored);
    }

    [Fact]
    public async Task ElevatedTargetIsRejectedBeforeClipboardOrInput()
    {
        var clipboard = new FakeClipboard("before", "selected");
        var input = new FakeInput { ValidationFailure = SelectedTextCaptureFailure.ElevatedTarget };
        var provider = Provider(clipboard, input);

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.Equal(SelectedTextCaptureFailure.ElevatedTarget, result.FailureReason);
        Assert.False(result.ClipboardFallbackUsed);
        Assert.False(clipboard.CaptureCalled);
        Assert.Equal(0, input.CopyCalls);
    }

    [Fact]
    public async Task CopyWaitsForExactTargetForeground()
    {
        var clipboard = new FakeClipboard("before", "selected");
        var input = new FakeInput(() => clipboard.Sequence++)
        {
            ForegroundChecksBeforeSuccess = 2
        };
        var provider = Provider(clipboard, input);

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(input.TargetFocusRequested);
        Assert.True(input.ForegroundChecks >= 3);
        Assert.Equal(1, input.CopyCalls);
    }

    [Fact]
    public async Task ChromiumStyleDelayedChildFocusSucceedsBeforeCopy()
    {
        var clipboard = new FakeClipboard("before", "selected");
        var input = new FakeInput(() => clipboard.Sequence++)
        {
            ForegroundChecksBeforeSuccess = 3,
            PendingFocusState = GuiFocusRestoreState.ChildNotFocused
        };
        var provider = Provider(clipboard, input, focusTimeout: TimeSpan.FromSeconds(10));

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(input.ForegroundChecks >= 4);
        Assert.Equal(1, input.CopyCalls);
    }

    [Fact]
    public async Task FocusFailureDoesNotSendCopy()
    {
        var clipboard = new FakeClipboard("before", "selected");
        var input = new FakeInput { NeverForeground = true };
        var provider = Provider(clipboard, input);

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("top-level-focus-restore-timeout", result.DiagnosticCode);
        Assert.Equal(0, input.CopyCalls);
    }

    [Fact]
    public async Task ChildFocusFailureDoesNotSendCopy()
    {
        var clipboard = new FakeClipboard("before", "selected");
        var input = new FakeInput
        {
            NeverForeground = true,
            PendingFocusState = GuiFocusRestoreState.ChildNotFocused
        };
        var provider = Provider(clipboard, input);

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("child-focus-restore-timeout", result.DiagnosticCode);
        Assert.Equal(0, input.CopyCalls);
    }

    [Fact]
    public async Task NativeInputFailureUsesSafeDiagnosticWithoutClipboardRead()
    {
        var clipboard = new FakeClipboard("before", "old");
        var provider = Provider(
            clipboard,
            new FakeInput { SendFailure = "send-input-failed-87" });

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("send-input-failed-87", result.DiagnosticCode);
        Assert.False(clipboard.RestoreCalled);
    }

    [Fact]
    public async Task ConcurrentCaptureIsRejectedWithoutSecondCopy()
    {
        var clipboard = new FakeClipboard("before", "selected");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new FakeInput(() =>
        {
            release.Task.GetAwaiter().GetResult();
            clipboard.Sequence++;
        });
        var provider = Provider(clipboard, input);

        var first = Task.Run(() => provider.CaptureAsync(Request(), CancellationToken.None));
        await Task.Delay(30);
        var second = await provider.CaptureAsync(Request(), CancellationToken.None);
        release.TrySetResult();
        var firstResult = await first;

        Assert.True(firstResult.Success);
        Assert.False(second.Success);
        Assert.Equal("copy-already-running", second.DiagnosticCode);
        Assert.Equal(1, input.CopyCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ZeroOrNegativeFocusTimeoutThrows(int milliseconds)
    {
        var clipboard = new FakeClipboard("before", "selected");
        var input = new FakeInput();
        var timeout = TimeSpan.FromMilliseconds(milliseconds);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => Provider(clipboard, input, focusTimeout: timeout));

        Assert.Equal("focusTimeout", ex.ParamName);
    }

    private static WindowsClipboardCopySelectedTextProvider Provider(
        FakeClipboard clipboard,
        FakeInput input,
        TimeSpan? focusTimeout = null) => new(() => 99, clipboard, input, focusTimeout: focusTimeout);

    private static SelectedTextCaptureRequest Request() => new(
        Foreground, PrivacySettings.Default, 1_000, true);

    private sealed class FakeClipboard(string before, string copied) : IClipboardCaptureBackend
    {
        public uint Sequence { get; set; } = 10;
        public bool CaptureCalled { get; private set; }
        public bool RestoreCalled { get; private set; }
        public bool RaceBeforeRestore { get; init; }
        public object SnapshotState { get; init; } = before;
        public object? RestoredState { get; private set; }

        public uint GetSequenceNumber() => Sequence;

        public ClipboardCaptureSnapshot? TryCapture()
        {
            CaptureCalled = true;
            return new ClipboardCaptureSnapshot(Sequence, SnapshotState);
        }

        public string? TryReadUnicodeText() => copied;

        public bool TryRestore(ClipboardCaptureSnapshot snapshot, uint expectedSequence)
        {
            RestoreCalled = true;
            RestoredState = snapshot.State;
            if (RaceBeforeRestore)
            {
                Sequence++;
            }

            return Sequence == expectedSequence && ReferenceEquals(snapshot.State, SnapshotState);
        }
    }

    private sealed class FakeInput(Action? onCopy = null) : IFixedCopyInputSender
    {
        public string? LastSendFailure => SendFailure;
        public SelectedTextCaptureFailure? ValidationFailure { get; init; }
        public string? SendFailure { get; init; }
        public int CopyCalls { get; private set; }
        public int ForegroundChecks { get; private set; }
        public int ForegroundChecksBeforeSuccess { get; init; }
        public bool NeverForeground { get; init; }
        public GuiFocusRestoreState PendingFocusState { get; init; } =
            GuiFocusRestoreState.TopLevelNotForeground;
        public bool TargetFocusRequested { get; private set; }
        public bool FocusRestored { get; private set; }

        public SelectedTextCaptureFailure? ValidateTarget(ActiveWindowReference target) =>
            ValidationFailure;

        public bool TryRestoreTargetFocus(
            ActiveWindowReference target,
            CancellationToken cancellationToken)
        {
            TargetFocusRequested = true;
            return !NeverForeground;
        }

        public GuiFocusRestoreState GetFocusState(ActiveWindowReference target)
        {
            ForegroundChecks++;
            return !NeverForeground && ForegroundChecks > ForegroundChecksBeforeSuccess
                ? GuiFocusRestoreState.Ready
                : PendingFocusState;
        }

        public bool SendCopy(ActiveWindowReference target)
        {
            CopyCalls++;
            onCopy?.Invoke();
            return SendFailure is null;
        }

        public void RestoreAedaFocus() => FocusRestored = true;
    }
}
