using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Infrastructure.Context;
using PersonalAI.Infrastructure.Windows;

namespace PersonalAI.Tests.Ui;

[Collection("NativeUiAutomation")]
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

    [Theory]
    [InlineData(13, true)]
    [InlineData(0xC000, true)]
    [InlineData(2, false)]
    [InlineData(14, false)]
    public void NativeSnapshotAcceptsOnlyRestorableHGlobalFormats(uint format, bool expected)
    {
        var snapshot = typeof(WindowsClipboardCopySelectedTextProvider).GetNestedType(
            "NativeClipboardSnapshot",
            System.Reflection.BindingFlags.NonPublic)!;
        var method = snapshot.GetMethod(
            "IsSupportedHGlobalFormat",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        Assert.Equal(expected, method.Invoke(null, [format]));
    }

    [Fact]
    public void ClipboardFormatEnumerationZeroWithNoErrorIsEndOfList()
    {
        var calls = 0;
        System.Runtime.InteropServices.Marshal.SetLastPInvokeError(87);

        var first = WindowsClipboardCopySelectedTextProvider.GetNextClipboardFormat(
            0,
            Enumerate);
        var end = WindowsClipboardCopySelectedTextProvider.GetNextClipboardFormat(
            first.Format,
            Enumerate);

        Assert.Equal((13u, 0), first);
        Assert.Equal((0u, 0), end);
        return;

        uint Enumerate(uint _)
        {
            calls++;
            return calls == 1 ? 13u : 0u;
        }
    }

    [Fact]
    public void ClipboardFormatEnumerationFailureBeforeFirstFormatIsNotEndOfList()
    {
        var result = WindowsClipboardCopySelectedTextProvider.GetNextClipboardFormat(
            0,
            _ =>
            {
                System.Runtime.InteropServices.Marshal.SetLastPInvokeError(5);
                return 0;
            });

        Assert.Equal((0u, 5), result);
    }

    [Fact]
    public void ClipboardFormatEnumerationFailureAfterAFormatIsNotEndOfList()
    {
        var results = new Queue<(uint Format, int Error)>(
        [
            (13, 0),
            (0, 5)
        ]);

        var first = WindowsClipboardCopySelectedTextProvider.GetNextClipboardFormat(
            0,
            Enumerate);
        var failure = WindowsClipboardCopySelectedTextProvider.GetNextClipboardFormat(
            first.Format,
            Enumerate);

        Assert.Equal((13u, 0), first);
        Assert.Equal((0u, 5), failure);
        return;

        uint Enumerate(uint _)
        {
            var result = results.Dequeue();
            System.Runtime.InteropServices.Marshal.SetLastPInvokeError(result.Error);
            return result.Format;
        }
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
        Assert.All(clipboard.ReadAttempts, state => Assert.True(state.IsOwnedBy(Foreground.ProcessId)));
    }

    [Fact]
    public async Task UnrelatedUpdateBeforeTargetCopyIsNeverReadAsCopy()
    {
        var clipboard = new FakeClipboard("before", "selected");
        var input = new FakeInput(() => clipboard.QueueStates(
            State(11, 700, 7),
            State(11, 700, 7),
            State(11, 700, 7),
            State(11, 700, 7),
            State(12, 420, Foreground.ProcessId)));
        var provider = Provider(clipboard, input);

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("selected", result.Text);
        Assert.DoesNotContain(clipboard.ReadAttempts, state => state.OwnerProcessId == 7);
        Assert.All(clipboard.ReadAttempts, state => Assert.Equal(Foreground.ProcessId, state.OwnerProcessId));
    }

    [Fact]
    public async Task ExternalUpdateDuringTargetStabilizationIsPreserved()
    {
        var clipboard = new FakeClipboard("before", "selected");
        var external = State(12, 700, 7);
        var input = new FakeInput(() => clipboard.QueueStates(
            State(11, 420, Foreground.ProcessId),
            State(11, 420, Foreground.ProcessId),
            external));
        var provider = Provider(clipboard, input);

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(SelectedTextCaptureFailure.SafeFailure, result.FailureReason);
        Assert.Equal("clipboard-copy-unattributed", result.DiagnosticCode);
        Assert.Empty(clipboard.ReadAttempts);
        Assert.False(clipboard.RestoreCalled);
        Assert.Equal(external, clipboard.State);
    }

    [Fact]
    public async Task StableExternalUpdateIsNeverAcceptedAsCopy()
    {
        var clipboard = new FakeClipboard("before", "external");
        var external = State(11, 700, 7);
        var provider = Provider(
            clipboard,
            new FakeInput(() => clipboard.State = external));

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(SelectedTextCaptureFailure.SafeFailure, result.FailureReason);
        Assert.Equal("clipboard-copy-unattributed", result.DiagnosticCode);
        Assert.Empty(clipboard.ReadAttempts);
        Assert.False(clipboard.RestoreCalled);
        Assert.Equal(external, clipboard.State);
    }

    [Fact]
    public async Task MultipleTargetChangesNeverAcceptInterleavedExternalOwner()
    {
        var clipboard = new FakeClipboard("before", "selected");
        var accepted = State(14, 421, Foreground.ProcessId);
        var input = new FakeInput(() => clipboard.QueueStates(
            State(11, 420, Foreground.ProcessId),
            State(12, 420, Foreground.ProcessId),
            State(13, 700, 7),
            accepted));
        var provider = Provider(clipboard, input);

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("selected", result.Text);
        Assert.DoesNotContain(clipboard.ReadAttempts, state => state.OwnerProcessId == 7);
        Assert.All(clipboard.ReadAttempts, state => Assert.Equal(accepted, state));
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
        Assert.False(clipboard.RestoreMutated);
    }

    [Fact]
    public async Task ExternalUpdateAfterAcceptedCopyIsNotOverwrittenOrSurfaced()
    {
        var clipboard = new FakeClipboard("before", "selected");
        var external = State(12, 700, 7);
        clipboard.OnRestoreAttempt = () => clipboard.State = external;
        var provider = Provider(clipboard, new FakeInput(() => clipboard.Sequence++));

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Text);
        Assert.Equal(SelectedTextCaptureFailure.ClipboardRestoreFailed, result.FailureReason);
        Assert.True(clipboard.RestoreCalled);
        Assert.False(clipboard.RestoreMutated);
        Assert.Equal(external, clipboard.State);
    }

    [Fact]
    public async Task ClipboardOwnerMismatchFailsClosedWithoutReadOrRestore()
    {
        var clipboard = new FakeClipboard("before", "external");
        var provider = Provider(
            clipboard,
            new FakeInput(() => clipboard.State = State(11, 700, 7)));

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(SelectedTextCaptureFailure.SafeFailure, result.FailureReason);
        Assert.Empty(clipboard.ReadAttempts);
        Assert.False(clipboard.RestoreCalled);
    }

    [Fact]
    public async Task ClipboardOwnerUnavailableFailsClosedWithoutReadOrRestore()
    {
        var clipboard = new FakeClipboard("before", "ownerless");
        var provider = Provider(
            clipboard,
            new FakeInput(() => clipboard.State = State(11, 0, 0)));

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(SelectedTextCaptureFailure.SafeFailure, result.FailureReason);
        Assert.Empty(clipboard.ReadAttempts);
        Assert.False(clipboard.RestoreCalled);
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
        Assert.True(clipboard.RestoreMutated);
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
        Assert.True(clipboard.RestoreMutated);
        Assert.True(input.FocusRestored);
    }

    [Theory]
    [InlineData("clipboard-format-unsupported")]
    [InlineData("clipboard-format-unavailable")]
    [InlineData("clipboard-format-enumeration-failed")]
    [InlineData("clipboard-changed-during-snapshot")]
    public async Task IncompleteSnapshotAbortsBeforeCopy(string failureCode)
    {
        var clipboard = new FakeClipboard("before", "selected")
        {
            CaptureFailureCode = failureCode
        };
        var input = new FakeInput();
        var provider = Provider(clipboard, input);

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(failureCode, result.DiagnosticCode);
        Assert.Equal(0, input.CopyCalls);
        Assert.False(clipboard.RestoreCalled);
    }

    [Fact]
    public async Task EnumerationFailureAfterFormatsDiscardsPartialSnapshotBeforeCopy()
    {
        var partialFormats = new Dictionary<string, byte[]>
        {
            ["UnicodeText"] = [1, 2]
        };
        var clipboard = new FakeClipboard("before", "selected")
        {
            CaptureFailureCode = "clipboard-format-enumeration-failed",
            SnapshotState = partialFormats
        };
        var input = new FakeInput();
        var provider = Provider(clipboard, input);

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("clipboard-format-enumeration-failed", result.DiagnosticCode);
        Assert.Equal(0, input.CopyCalls);
        Assert.False(clipboard.RestoreCalled);
        Assert.Null(clipboard.RestoredState);
    }

    [Fact]
    public async Task MissingRestoreOwnerAbortsBeforeSnapshotOrCopy()
    {
        var clipboard = new FakeClipboard("before", "selected") { CanRestore = false };
        var input = new FakeInput();
        var provider = Provider(clipboard, input);

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.Equal("clipboard-owner-unavailable", result.DiagnosticCode);
        Assert.False(clipboard.CaptureCalled);
        Assert.Equal(0, input.CopyCalls);
    }

    [Fact]
    public async Task CancellationAfterSnapshotButBeforeCopyLeavesClipboardUnchanged()
    {
        using var cancellation = new CancellationTokenSource();
        var clipboard = new FakeClipboard("before", "selected")
        {
            OnCapture = cancellation.Cancel
        };
        var input = new FakeInput();
        var provider = Provider(clipboard, input);

        var result = await provider.CaptureAsync(Request(), cancellation.Token);

        Assert.Equal(SelectedTextCaptureFailure.Cancelled, result.FailureReason);
        Assert.Equal(0, input.CopyCalls);
        Assert.False(clipboard.RestoreCalled);
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
    public async Task PrivacyExcludedTargetIsRejectedBeforeClipboardOrInput()
    {
        var clipboard = new FakeClipboard("before", "selected");
        var input = new FakeInput();
        var provider = Provider(clipboard, input);
        var request = Request() with
        {
            Foreground = Foreground with { ProcessName = "1Password", WindowTitle = "Vault" }
        };

        var result = await provider.CaptureAsync(request, CancellationToken.None);

        Assert.Equal(SelectedTextCaptureFailure.PrivacyBlocked, result.FailureReason);
        Assert.False(clipboard.CaptureCalled);
        Assert.Equal(0, input.CopyCalls);
    }

    [Fact]
    public async Task PasswordTargetIsRejectedBeforeClipboardOrInput()
    {
        var clipboard = new FakeClipboard("before", "selected");
        var input = new FakeInput { ValidationFailure = SelectedTextCaptureFailure.PasswordControl };
        var provider = Provider(clipboard, input);

        var result = await provider.CaptureAsync(Request(), CancellationToken.None);

        Assert.Equal(SelectedTextCaptureFailure.PasswordControl, result.FailureReason);
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
        var provider = Provider(clipboard, input);

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

    private static WindowsClipboardCopySelectedTextProvider Provider(
        FakeClipboard clipboard,
        FakeInput input) => new(() => 99, clipboard, input);

    private static SelectedTextCaptureRequest Request() => new(
        Foreground, PrivacySettings.Default, 1_000, true);

    private static ClipboardCopyState State(uint sequence, nint owner, uint processId) =>
        new(sequence, owner, processId);

    private sealed class FakeClipboard(string before, string copied) : IClipboardCaptureBackend
    {
        public bool CanRestore { get; init; } = true;
        public string? CaptureFailureCode { get; init; }
        public ClipboardCopyState State { get; set; } = new(10, 420, Foreground.ProcessId);
        public uint Sequence
        {
            get => State.SequenceNumber;
            set => State = State with { SequenceNumber = value };
        }

        public bool CaptureCalled { get; private set; }
        public bool RestoreCalled { get; private set; }
        public bool RestoreMutated { get; private set; }
        public bool RaceBeforeRestore { get; init; }
        public object SnapshotState { get; init; } = before;
        public object? RestoredState { get; private set; }
        public Action? OnCapture { get; init; }
        public Action? OnRestoreAttempt { get; set; }
        public List<ClipboardCopyState> ReadAttempts { get; } = [];
        private Queue<ClipboardCopyState?> StateSamples { get; } = new();

        public uint GetSequenceNumber() => Sequence;

        public ClipboardCopyState? TryGetState()
        {
            if (StateSamples.TryDequeue(out var sampled))
            {
                if (sampled is { } state)
                {
                    State = state;
                }

                return sampled;
            }

            return State;
        }

        public void QueueStates(params ClipboardCopyState?[] states)
        {
            foreach (var state in states)
            {
                StateSamples.Enqueue(state);
            }
        }

        public ClipboardCaptureSnapshot? TryCapture()
        {
            CaptureCalled = true;
            OnCapture?.Invoke();
            if (CaptureFailureCode is not null)
            {
                return null;
            }

            return new ClipboardCaptureSnapshot(Sequence, SnapshotState);
        }

        public string? TryReadUnicodeText(ClipboardCopyState expectedState)
        {
            ReadAttempts.Add(expectedState);
            return State == expectedState ? copied : null;
        }

        public bool TryRestore(
            ClipboardCaptureSnapshot snapshot,
            ClipboardCopyState expectedState)
        {
            RestoreCalled = true;
            if (RaceBeforeRestore)
            {
                Sequence++;
            }

            OnRestoreAttempt?.Invoke();
            if (State != expectedState || !ReferenceEquals(snapshot.State, SnapshotState))
            {
                return false;
            }

            RestoreMutated = true;
            RestoredState = snapshot.State;
            return true;
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
