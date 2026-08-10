#if WINDOWS
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Infrastructure.Context;

namespace PersonalAI.Infrastructure.Windows;

public sealed record ClipboardCaptureSnapshot(uint SequenceNumber, object State);

public readonly record struct ClipboardCopyState(
    uint SequenceNumber,
    nint OwnerWindow,
    uint OwnerProcessId)
{
    public bool IsOwnedBy(uint processId) =>
        OwnerWindow != 0 && OwnerProcessId != 0 && OwnerProcessId == processId;
}

public interface IClipboardCaptureBackend
{
    bool CanRestore { get; }
    string? CaptureFailureCode { get; }
    uint GetSequenceNumber();
    ClipboardCopyState? TryGetState();
    ClipboardCaptureSnapshot? TryCapture();
    string? TryReadUnicodeText(ClipboardCopyState expectedState);
    bool TryRestore(ClipboardCaptureSnapshot snapshot, ClipboardCopyState expectedState);
}

public interface IFixedCopyInputSender
{
    string? LastSendFailure { get; }
    SelectedTextCaptureFailure? ValidateTarget(ActiveWindowReference target);
    bool TryRestoreTargetFocus(
        ActiveWindowReference target,
        CancellationToken cancellationToken);
    GuiFocusRestoreState GetFocusState(ActiveWindowReference target);
    bool SendCopy(ActiveWindowReference target);
    void RestoreAedaFocus();
}

public enum GuiFocusRestoreState
{
    TargetInvalid,
    TopLevelNotForeground,
    ChildNotFocused,
    Ready
}

public sealed class WindowsClipboardCopySelectedTextProvider(
    Func<nint> getAedaWindowHandle,
    IClipboardCaptureBackend? clipboard = null,
    IFixedCopyInputSender? input = null) : IClipboardCopySelectedTextProvider
{
    private static readonly TimeSpan FocusTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ClipboardTimeout = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ClipboardStableDelay = TimeSpan.FromMilliseconds(60);
    private readonly IClipboardCaptureBackend _clipboard = clipboard ?? new NativeClipboardBackend(getAedaWindowHandle);
    private readonly IFixedCopyInputSender _input = input ?? new NativeFixedCopyInputSender(getAedaWindowHandle);
    private readonly SemaphoreSlim _captureGate = new(1, 1);

    public async Task<SelectedTextCaptureResult> CaptureAsync(
        SelectedTextCaptureRequest request,
        CancellationToken cancellationToken)
    {
        if (!await _captureGate.WaitAsync(0, cancellationToken))
        {
            return Fail(request, SelectedTextCaptureFailure.SafeFailure, "copy-already-running", used: false);
        }

        var target = request.Foreground;
        try
        {
            if (PrivacyExclusionMatcher.IsSensitiveWindow(
                    target.ProcessName, target.WindowTitle, request.Privacy.ExcludedApplications))
            {
                return Fail(request, SelectedTextCaptureFailure.PrivacyBlocked, "privacy-blocked");
            }

            if (_input.ValidateTarget(target) is { } targetFailure)
            {
                return Fail(
                    request,
                    targetFailure,
                    targetFailure == SelectedTextCaptureFailure.ElevatedTarget
                        ? "elevated-target"
                        : "target-invalidated",
                    used: false);
            }

            if (!_clipboard.CanRestore)
            {
                return Fail(
                    request,
                    SelectedTextCaptureFailure.SafeFailure,
                    "clipboard-owner-unavailable",
                    used: false);
            }

            ClipboardCaptureSnapshot? snapshot = null;
            ClipboardCopyState? copyState = null;
            var copySent = false;
            var restored = true;
            string? text = null;
            var failure = SelectedTextCaptureFailure.SafeFailure;
            var diagnostic = "copy-safe-failure";
            try
            {
                snapshot = await Task.Run(_clipboard.TryCapture, cancellationToken);
                if (snapshot is null)
                {
                    var code = _clipboard.CaptureFailureCode ?? "clipboard-busy";
                    return Fail(
                        request,
                        code == "clipboard-busy"
                            ? SelectedTextCaptureFailure.ClipboardBusy
                            : SelectedTextCaptureFailure.SafeFailure,
                        code);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (_clipboard.GetSequenceNumber() != snapshot.SequenceNumber)
                {
                    failure = SelectedTextCaptureFailure.SafeFailure;
                    diagnostic = "clipboard-changed-before-copy";
                }
                else if (await RestoreTargetFocusAsync(target, cancellationToken) is { } focusFailure)
                {
                    diagnostic = focusFailure;
                }
                else if (_input.ValidateTarget(target) is not null || !_input.SendCopy(target))
                {
                    diagnostic = _input.LastSendFailure ?? "copy-not-sent";
                }
                else
                {
                    copySent = true;
                    var (clipboardChanged, acceptedState, copiedText) =
                        await WaitForCopiedTextAsync(
                            snapshot.SequenceNumber,
                            target.ProcessId,
                            cancellationToken);
                    copyState = acceptedState;
                    text = copiedText;
                    if (copyState is null)
                    {
                        failure = clipboardChanged
                            ? SelectedTextCaptureFailure.SafeFailure
                            : SelectedTextCaptureFailure.ClipboardDidNotChange;
                        diagnostic = clipboardChanged
                            ? "clipboard-copy-unattributed"
                            : "clipboard-unchanged";
                    }
                    else
                    {
                        text = Bound(text, request.MaxCharacters);
                        failure = string.IsNullOrWhiteSpace(text)
                            ? SelectedTextCaptureFailure.NoSelection
                            : SelectedTextCaptureFailure.None;
                        diagnostic = failure == SelectedTextCaptureFailure.None
                            ? "clipboard-selection"
                            : text is null
                                ? "clipboard-text-unavailable"
                                : "copied-text-empty";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                failure = SelectedTextCaptureFailure.Cancelled;
                diagnostic = "cancelled";
            }
            catch
            {
                failure = SelectedTextCaptureFailure.SafeFailure;
                diagnostic = "copy-safe-failure";
            }
            finally
            {
                if (snapshot is not null && copySent && copyState is null &&
                    _clipboard.TryGetState() is { } currentState &&
                    currentState.SequenceNumber != snapshot.SequenceNumber &&
                    currentState.IsOwnedBy(target.ProcessId))
                {
                    copyState = currentState;
                }

                if (snapshot is not null && copyState is { } acceptedState)
                {
                    restored = _clipboard.TryRestore(snapshot, acceptedState);
                    if (!restored)
                    {
                        Debug.WriteLine("AEDA clipboard restoration was skipped or failed.");
                    }
                }

                _input.RestoreAedaFocus();
            }

            if (!restored)
            {
                return new SelectedTextCaptureResult(
                    false, null, SelectedTextCaptureSource.None,
                    target.ProcessName, DateTimeOffset.UtcNow,
                    SelectedTextCaptureFailure.ClipboardRestoreFailed,
                    true, false, "clipboard-restore-failed");
            }

            return failure == SelectedTextCaptureFailure.None
                ? new SelectedTextCaptureResult(
                    true, text, SelectedTextCaptureSource.ClipboardCopyFallback,
                    target.ProcessName, DateTimeOffset.UtcNow,
                    failure, true, true, diagnostic)
                : Fail(request, failure, diagnostic);
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private async Task<string?> RestoreTargetFocusAsync(
        ActiveWindowReference target,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var lastState = GuiFocusRestoreState.TopLevelNotForeground;
        while (stopwatch.Elapsed < FocusTimeout)
        {
            if (_input.ValidateTarget(target) is not null)
            {
                return "target-invalidated";
            }

            lastState = _input.GetFocusState(target);
            if (lastState == GuiFocusRestoreState.Ready)
            {
                return null;
            }

            if (lastState == GuiFocusRestoreState.TargetInvalid)
            {
                return "target-invalidated";
            }

            _ = _input.TryRestoreTargetFocus(target, cancellationToken);
            await Task.Delay(20, cancellationToken);
        }

        lastState = _input.GetFocusState(target);
        return lastState switch
        {
            GuiFocusRestoreState.Ready => null,
            GuiFocusRestoreState.TargetInvalid => "target-invalidated",
            GuiFocusRestoreState.TopLevelNotForeground => "top-level-focus-restore-timeout",
            _ => "child-focus-restore-timeout"
        };
    }

    private async Task<(bool Changed, ClipboardCopyState? State, string? Text)> WaitForCopiedTextAsync(
        uint originalSequence,
        uint targetProcessId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        ClipboardCopyState? candidate = null;
        var changed = false;
        var stableSince = TimeSpan.Zero;
        while (stopwatch.Elapsed < ClipboardTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_clipboard.TryGetState() is not { } current)
            {
                candidate = null;
            }
            else if (current.SequenceNumber != originalSequence)
            {
                changed = true;
                if (!current.IsOwnedBy(targetProcessId))
                {
                    candidate = null;
                }
                else
                {
                    if (candidate != current)
                    {
                        candidate = current;
                        stableSince = stopwatch.Elapsed;
                    }

                    if (stopwatch.Elapsed - stableSince >= ClipboardStableDelay)
                    {
                        var text = await Task.Run(
                            () => _clipboard.TryReadUnicodeText(current),
                            cancellationToken);
                        if (text is not null)
                        {
                            return (true, current, text);
                        }

                        if (_clipboard.TryGetState() != current)
                        {
                            candidate = null;
                        }
                    }
                }
            }
            else
            {
                candidate = null;
            }

            await Task.Delay(20, cancellationToken);
        }

        if (candidate is { } accepted && _clipboard.TryGetState() == accepted)
        {
            return (changed, accepted, null);
        }

        if (!changed && _clipboard.GetSequenceNumber() != originalSequence)
        {
            changed = true;
        }

        return (changed, null, null);
    }

    internal static (uint Format, int Error) GetNextClipboardFormat(
        uint format,
        Func<uint, uint> enumerate)
    {
        Marshal.SetLastPInvokeError(0);
        var next = enumerate(format);
        return (next, next == 0 ? Marshal.GetLastPInvokeError() : 0);
    }

    private static string? Bound(string? text, int maxCharacters)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value) || maxCharacters <= 0)
        {
            return null;
        }

        var lines = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Take(2_000);
        value = string.Join(Environment.NewLine, lines);
        return value.Length <= maxCharacters ? value : value[..maxCharacters];
    }

    private static SelectedTextCaptureResult Fail(
        SelectedTextCaptureRequest request,
        SelectedTextCaptureFailure reason,
        string code,
        bool used = true) => new(
            false, null, SelectedTextCaptureSource.None,
            request.Foreground.ProcessName, DateTimeOffset.UtcNow,
            reason, used, true, code);

    private sealed record ClipboardFormat(uint Id, byte[] Bytes);

    private sealed record NativeClipboardSnapshot(uint SequenceNumber, IReadOnlyList<ClipboardFormat> Formats)
    {
        private const nuint MaxFormatBytes = 64 * 1024 * 1024;

        public static NativeClipboardSnapshot? TryCapture(out string? failureCode)
        {
            failureCode = null;
            if (!Native.TryOpenClipboard(0))
            {
                failureCode = "clipboard-busy";
                return null;
            }

            try
            {
                var sequence = Native.GetClipboardSequenceNumber();
                var formats = new List<ClipboardFormat>();
                uint format = 0;
                while (true)
                {
                    var (nextFormat, error) = GetNextClipboardFormat(
                        format,
                        Native.EnumClipboardFormats);
                    if (nextFormat == 0)
                    {
                        if (error != 0)
                        {
                            failureCode = "clipboard-format-enumeration-failed";
                            return null;
                        }

                        break;
                    }

                    format = nextFormat;
                    if (!IsSupportedHGlobalFormat(format))
                    {
                        failureCode = "clipboard-format-unsupported";
                        return null;
                    }

                    var handle = Native.GetClipboardData(format);
                    var size = handle == 0 ? 0 : Native.GlobalSize(handle);
                    if (size == 0 || size > MaxFormatBytes)
                    {
                        failureCode = "clipboard-format-unavailable";
                        return null;
                    }

                    var pointer = Native.GlobalLock(handle);
                    if (pointer == 0)
                    {
                        failureCode = "clipboard-format-unavailable";
                        return null;
                    }

                    try
                    {
                        var bytes = new byte[(int)size];
                        Marshal.Copy(pointer, bytes, 0, bytes.Length);
                        formats.Add(new ClipboardFormat(format, bytes));
                    }
                    finally
                    {
                        _ = Native.GlobalUnlock(handle);
                    }
                }

                if (Native.GetClipboardSequenceNumber() != sequence)
                {
                    failureCode = "clipboard-changed-during-snapshot";
                    return null;
                }

                return new NativeClipboardSnapshot(sequence, formats);
            }
            finally
            {
                _ = Native.CloseClipboard();
            }
        }

        public static string? TryReadUnicodeText(ClipboardCopyState expectedState)
        {
            const uint unicodeText = 13;
            if (!Native.TryOpenClipboard(0))
            {
                return null;
            }

            try
            {
                if (!Native.IsCurrentClipboardState(expectedState))
                {
                    return null;
                }

                var handle = Native.GetClipboardData(unicodeText);
                var pointer = handle == 0 ? 0 : Native.GlobalLock(handle);
                if (pointer == 0)
                {
                    return null;
                }

                try
                {
                    var text = Marshal.PtrToStringUni(pointer);
                    return Native.IsCurrentClipboardState(expectedState) ? text : null;
                }
                finally
                {
                    _ = Native.GlobalUnlock(handle);
                }
            }
            finally
            {
                _ = Native.CloseClipboard();
            }
        }

        public static bool TryRestore(
            NativeClipboardSnapshot snapshot,
            ClipboardCopyState expectedState,
            nint owner)
        {
            if (owner == 0 || !Native.IsWindow(owner) ||
                !Native.IsCurrentClipboardState(expectedState))
            {
                return false;
            }

            var prepared = new List<PreparedClipboardFormat>(snapshot.Formats.Count);
            try
            {
                foreach (var format in snapshot.Formats)
                {
                    var handle = Native.GlobalAlloc(0x0002, (nuint)format.Bytes.Length);
                    if (handle == 0)
                    {
                        return false;
                    }

                    prepared.Add(new PreparedClipboardFormat(format.Id, handle));
                    var pointer = Native.GlobalLock(handle);
                    if (pointer == 0)
                    {
                        return false;
                    }

                    try
                    {
                        Marshal.Copy(format.Bytes, 0, pointer, format.Bytes.Length);
                    }
                    finally
                    {
                        _ = Native.GlobalUnlock(handle);
                    }
                }

                if (!Native.TryOpenClipboard(owner))
                {
                    return false;
                }

                try
                {
                    if (!Native.IsCurrentClipboardState(expectedState) || !Native.EmptyClipboard())
                    {
                        return false;
                    }

                    var restored = true;
                    foreach (var format in prepared)
                    {
                        if (Native.SetClipboardData(format.Id, format.Handle) != 0)
                        {
                            format.Transferred = true;
                        }
                        else
                        {
                            restored = false;
                        }
                    }

                    return restored;
                }
                finally
                {
                    _ = Native.CloseClipboard();
                }
            }
            finally
            {
                foreach (var format in prepared.Where(format => !format.Transferred))
                {
                    _ = Native.GlobalFree(format.Handle);
                }
            }
        }

        private static bool IsSupportedHGlobalFormat(uint format) => format switch
        {
            1 or 4 or 5 or 6 or 7 or 8 or 10 or 11 or 12 or 13 or 15 or 16 or 17 or 0x81 => true,
            >= 0xC000 => true,
            _ => false
        };

        private sealed class PreparedClipboardFormat(uint id, nint handle)
        {
            public uint Id { get; } = id;
            public nint Handle { get; } = handle;
            public bool Transferred { get; set; }
        }
    }

    private sealed class NativeClipboardBackend(Func<nint> getAedaWindowHandle) : IClipboardCaptureBackend
    {
        public bool CanRestore
        {
            get
            {
                var owner = getAedaWindowHandle();
                return owner != 0 && Native.IsWindow(owner);
            }
        }

        public string? CaptureFailureCode { get; private set; }

        public uint GetSequenceNumber() => Native.GetClipboardSequenceNumber();

        public ClipboardCopyState? TryGetState() => Native.TryGetClipboardState();

        public ClipboardCaptureSnapshot? TryCapture()
        {
            CaptureFailureCode = null;
            var snapshot = NativeClipboardSnapshot.TryCapture(out var failureCode);
            CaptureFailureCode = failureCode;
            return snapshot is null
                ? null
                : new ClipboardCaptureSnapshot(snapshot.SequenceNumber, snapshot);
        }

        public string? TryReadUnicodeText(ClipboardCopyState expectedState) =>
            NativeClipboardSnapshot.TryReadUnicodeText(expectedState);

        public bool TryRestore(
            ClipboardCaptureSnapshot snapshot,
            ClipboardCopyState expectedState) =>
            snapshot.State is NativeClipboardSnapshot native &&
            NativeClipboardSnapshot.TryRestore(native, expectedState, getAedaWindowHandle());
    }

    private sealed class NativeFixedCopyInputSender : IFixedCopyInputSender
    {
        private readonly Func<nint> _getAedaWindowHandle;
        private readonly WindowsGuiFocusController _focus = new();

        public string? LastSendFailure { get; private set; }

        public NativeFixedCopyInputSender(Func<nint> getAedaWindowHandle) =>
            _getAedaWindowHandle = getAedaWindowHandle;

        public SelectedTextCaptureFailure? ValidateTarget(ActiveWindowReference target)
        {
            if (!Native.IsSameWindow(target))
            {
                return SelectedTextCaptureFailure.SafeFailure;
            }

            return Native.IsHigherIntegrity(target.ProcessId)
                ? SelectedTextCaptureFailure.ElevatedTarget
                : null;
        }

        public bool SendCopy(ActiveWindowReference target)
        {
            LastSendFailure = null;
            if (_focus.GetState(target) != GuiFocusRestoreState.Ready)
            {
                LastSendFailure = "focus-lost-before-copy";
                return false;
            }

            return Native.SendCopy(target, failure => LastSendFailure = failure);
        }

        public bool TryRestoreTargetFocus(
            ActiveWindowReference target,
            CancellationToken cancellationToken) =>
            _focus.TryRestore(target, cancellationToken);

        public GuiFocusRestoreState GetFocusState(ActiveWindowReference target) =>
            _focus.GetState(target);

        public void RestoreAedaFocus() => Native.TryFocus(_getAedaWindowHandle());
    }

    private static class Native
    {
        public static bool IsSameWindow(ActiveWindowReference target) =>
            target.WindowHandle != 0 && IsWindow(target.WindowHandle) &&
            GetProcessId(target.WindowHandle) == target.ProcessId;

        public static bool SendCopy(
            ActiveWindowReference target,
            Action<string> reportFailure)
        {
            if (GetForegroundWindow() != target.WindowHandle)
            {
                reportFailure("foreground-lost-before-copy");
                return false;
            }

            var focusedWindow = target.GuiThread?.FocusedWindow ?? 0;
            if (IsNativePasswordEdit(focusedWindow))
            {
                reportFailure("native-password-control");
                return false;
            }

            try
            {
                var focused = focusedWindow != 0
                    ? AutomationElement.FromHandle(focusedWindow)
                    : AutomationElement.FocusedElement;
                if (focused is not null &&
                    focused.Current.ProcessId == target.ProcessId &&
                    focused.Current.IsPassword)
                {
                    reportFailure("uia-password-control");
                    return false;
                }
            }
            catch
            {
                // Native/Electron/Scintilla surfaces may not expose a UIA element.
                // Exact HWND focus is already verified; only a positive password
                // signal blocks the fixed Copy command.
            }

            Input[] inputs =
            [
                // Invocation hotkeys can still be physically held when AEDA reaches
                // the target. Release the fixed modifier set before sending Copy so
                // Electron and terminal surfaces do not receive Ctrl+Shift+C or a
                // Windows-key chord. No text or arbitrary input is synthesized.
                Input.Key(0x10, true),
                Input.Key(0x11, true),
                Input.Key(0x12, true),
                Input.Key(0x5B, true),
                Input.Key(0x5C, true),
                Input.Key(0x11, false),
                Input.Key(0x43, false),
                Input.Key(0x43, true),
                Input.Key(0x11, true)
            ];
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length)
            {
                return true;
            }

            reportFailure($"send-input-failed-{Marshal.GetLastWin32Error()}");
            return false;
        }

        private static bool IsNativePasswordEdit(nint window)
        {
            if (window == 0)
            {
                return false;
            }

            var className = new System.Text.StringBuilder(64);
            _ = GetClassName(window, className, className.Capacity);
            return NativePasswordControlPolicy.IsPasswordEdit(
                className.ToString(),
                GetWindowLongPtr(window, -16).ToInt64());
        }

        public static bool TryFocus(nint window) =>
            window != 0 && IsWindow(window) && SetForegroundWindow(window);

        public static bool IsHigherIntegrity(uint processId)
        {
            try
            {
                using var current = Process.GetCurrentProcess();
                using var target = Process.GetProcessById((int)processId);
                return Integrity(target.Handle) > Integrity(current.Handle);
            }
            catch
            {
                return true;
            }
        }

        private static int Integrity(nint process)
        {
            if (!OpenProcessToken(process, 0x0008, out var token))
            {
                throw new Win32Exception();
            }

            try
            {
                _ = GetTokenInformation(token, 25, 0, 0, out var length);
                var buffer = Marshal.AllocHGlobal(length);
                try
                {
                    if (!GetTokenInformation(token, 25, buffer, length, out _))
                    {
                        throw new Win32Exception();
                    }

                    var sid = Marshal.ReadIntPtr(buffer);
                    var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                    return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                _ = CloseHandle(token);
            }
        }

        public static bool TryOpenClipboard(nint owner)
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                if (OpenClipboard(owner))
                {
                    return true;
                }

                Thread.Sleep(15);
            }

            return false;
        }

        public static ClipboardCopyState? TryGetClipboardState()
        {
            var sequence = GetClipboardSequenceNumber();
            var owner = GetClipboardOwner();
            uint processId = 0;
            if (owner != 0 && GetWindowThreadProcessId(owner, out processId) == 0)
            {
                processId = 0;
            }

            var confirmedOwner = GetClipboardOwner();
            var confirmedSequence = GetClipboardSequenceNumber();
            return sequence == confirmedSequence && owner == confirmedOwner
                ? new ClipboardCopyState(sequence, owner, processId)
                : null;
        }

        public static bool IsCurrentClipboardState(ClipboardCopyState expectedState) =>
            TryGetClipboardState() == expectedState;

        private static uint GetProcessId(nint window)
        {
            _ = GetWindowThreadProcessId(window, out var processId);
            return processId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Input
        {
            public uint Type;
            public InputUnion Union;

            public static Input Key(ushort key, bool up) => new()
            {
                Type = 1,
                Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = up ? 2u : 0u } }
            };
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public KeyboardInput Keyboard;
            [FieldOffset(0)] public MouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public nuint ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseInput
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public nuint ExtraInfo;
        }

        [DllImport("user32.dll")] public static extern bool IsWindow(nint hWnd);
        [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hWnd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hWnd, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hWnd, System.Text.StringBuilder className, int maxCount);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(nint owner);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool CloseClipboard();
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] private static extern nint GetClipboardOwner();
        [DllImport("user32.dll", SetLastError = true)] public static extern uint EnumClipboardFormats(uint format);
        [DllImport("user32.dll")] public static extern nint GetClipboardData(uint format);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] public static extern nint SetClipboardData(uint format, nint handle);
        [DllImport("kernel32.dll")] public static extern nuint GlobalSize(nint handle);
        [DllImport("kernel32.dll")] public static extern nint GlobalLock(nint handle);
        [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(nint handle);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern nint GlobalAlloc(uint flags, nuint bytes);
        [DllImport("kernel32.dll")] public static extern nint GlobalFree(nint handle);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(nint token, int infoClass, nint info, int length, out int returnLength);
        [DllImport("advapi32.dll")] private static extern nint GetSidSubAuthorityCount(nint sid);
        [DllImport("advapi32.dll")] private static extern nint GetSidSubAuthority(nint sid, uint index);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    }
}

public interface IGuiFocusNative
{
    nint ForegroundWindow { get; }
    uint CurrentThreadId { get; }
    bool IsWindow(nint window);
    uint GetWindowThread(nint window, out uint processId);
    bool IsChild(nint parent, nint child);
    GuiThreadWindowSnapshot? GetGuiThread(uint threadId, uint processId);
    bool SetForegroundWindow(nint window);
    bool BringWindowToTop(nint window);
    bool AttachThreadInput(uint sourceThread, uint targetThread, bool attach);
    nint SetActiveWindow(nint window);
    nint SetFocus(nint window);
}

public static class NativePasswordControlPolicy
{
    public static bool IsPasswordEdit(string className, long style) =>
        (className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
         className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)) &&
        (style & 0x20) != 0;
}

public sealed class WindowsGuiFocusController(IGuiFocusNative? native = null)
{
    private readonly IGuiFocusNative _native = native ?? new Win32GuiFocusNative();

    public GuiFocusRestoreState GetState(ActiveWindowReference target)
    {
        if (!TryResolve(target, out var threadId, out var focusedWindow))
        {
            return target.GuiThread is null
                ? GuiFocusRestoreState.ChildNotFocused
                : GuiFocusRestoreState.TargetInvalid;
        }

        if (_native.ForegroundWindow != target.WindowHandle)
        {
            return GuiFocusRestoreState.TopLevelNotForeground;
        }

        var current = _native.GetGuiThread(threadId, target.ProcessId);
        return current is not null && FocusMatches(focusedWindow, current.FocusedWindow)
            ? GuiFocusRestoreState.Ready
            : GuiFocusRestoreState.ChildNotFocused;
    }

    public bool TryRestore(
        ActiveWindowReference target,
        CancellationToken cancellationToken)
    {
        if (!TryResolve(target, out var targetThread, out var focusedWindow))
        {
            return false;
        }

        _ = _native.SetForegroundWindow(target.WindowHandle);
        _ = _native.BringWindowToTop(target.WindowHandle);

        var sourceThread = _native.CurrentThreadId;
        var attached = sourceThread != targetThread;
        if (attached && !_native.AttachThreadInput(sourceThread, targetThread, true))
        {
            return false;
        }

        try
        {
            var active = IsOwnedTarget(target, target.GuiThread!.ActiveWindow)
                ? target.GuiThread.ActiveWindow
                : target.WindowHandle;
            _ = _native.SetActiveWindow(active);
            cancellationToken.ThrowIfCancellationRequested();
            _ = _native.SetFocus(focusedWindow);
            return true;
        }
        finally
        {
            if (attached)
            {
                _ = _native.AttachThreadInput(sourceThread, targetThread, false);
            }
        }
    }

    private bool TryResolve(
        ActiveWindowReference target,
        out uint threadId,
        out nint focusedWindow)
    {
        threadId = 0;
        focusedWindow = 0;
        var currentThread = _native.GetWindowThread(
            target.WindowHandle,
            out var processId);
        if (target.WindowHandle == 0 || !_native.IsWindow(target.WindowHandle) ||
            currentThread == 0 || processId != target.ProcessId ||
            target.GuiThread is not { } snapshot ||
            snapshot.ThreadId != currentThread || snapshot.ProcessId != processId)
        {
            return false;
        }

        threadId = currentThread;
        focusedWindow = IsOwnedTarget(target, snapshot.FocusedWindow)
            ? snapshot.FocusedWindow
            : IsOwnedTarget(target, snapshot.CaretWindow)
                ? snapshot.CaretWindow
                : 0;
        return focusedWindow != 0;
    }

    private bool IsOwnedTarget(ActiveWindowReference target, nint window)
    {
        if (window == 0 || !_native.IsWindow(window) ||
            _native.GetWindowThread(window, out var processId) == 0 ||
            processId != target.ProcessId)
        {
            return false;
        }

        return window == target.WindowHandle ||
            _native.IsChild(target.WindowHandle, window);
    }

    private bool FocusMatches(nint remembered, nint current) =>
        current == remembered ||
        remembered != 0 && current != 0 && _native.IsChild(remembered, current);

    private sealed class Win32GuiFocusNative : IGuiFocusNative
    {
        public nint ForegroundWindow => GetForegroundWindow();
        public uint CurrentThreadId => GetCurrentThreadId();
        public bool IsWindow(nint window) => IsWindowNative(window);
        public uint GetWindowThread(nint window, out uint processId) =>
            GetWindowThreadProcessId(window, out processId);
        public bool IsChild(nint parent, nint child) => IsChildNative(parent, child);
        public bool SetForegroundWindow(nint window) => SetForegroundWindowNative(window);
        public bool BringWindowToTop(nint window) => BringWindowToTopNative(window);
        public bool AttachThreadInput(uint sourceThread, uint targetThread, bool attach) =>
            AttachThreadInputNative(sourceThread, targetThread, attach);
        public nint SetActiveWindow(nint window) => SetActiveWindowNative(window);
        public nint SetFocus(nint window) => SetFocusNative(window);

        public GuiThreadWindowSnapshot? GetGuiThread(uint threadId, uint processId)
        {
            var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
            return GetGUIThreadInfo(threadId, ref info)
                ? new GuiThreadWindowSnapshot(
                    threadId,
                    processId,
                    info.ActiveWindow,
                    info.FocusedWindow,
                    info.CaptureWindow,
                    info.MenuOwnerWindow,
                    info.MoveSizeWindow,
                    info.CaretWindow,
                    DateTimeOffset.UtcNow)
                : null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            public int Size;
            public uint Flags;
            public nint ActiveWindow;
            public nint FocusedWindow;
            public nint CaptureWindow;
            public nint MenuOwnerWindow;
            public nint MoveSizeWindow;
            public nint CaretWindow;
            public Rect CaretRect;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", EntryPoint = "IsWindow")] private static extern bool IsWindowNative(nint window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
        [DllImport("user32.dll", EntryPoint = "IsChild")] private static extern bool IsChildNative(nint parent, nint child);
        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")] private static extern bool SetForegroundWindowNative(nint window);
        [DllImport("user32.dll", EntryPoint = "BringWindowToTop")] private static extern bool BringWindowToTopNative(nint window);
        [DllImport("user32.dll", EntryPoint = "AttachThreadInput")] private static extern bool AttachThreadInputNative(uint sourceThread, uint targetThread, bool attach);
        [DllImport("user32.dll", EntryPoint = "SetActiveWindow")] private static extern nint SetActiveWindowNative(nint window);
        [DllImport("user32.dll", EntryPoint = "SetFocus")] private static extern nint SetFocusNative(nint window);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    }
}
#endif
