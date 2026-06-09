using PersonalAI.Core.Context;

namespace PersonalAI.Tests.Context;

public sealed class ActiveWindowReferenceTrackerTests
{
    [Fact]
    public void TryRemember_IgnoresOwnWindow()
    {
        var tracker = new ActiveWindowReferenceTracker();
        var ownWindow = new ActiveWindowReference(
            WindowHandle: 10,
            ProcessId: 100,
            ProcessName: "PersonalAI",
            WindowTitle: "PersonalAI",
            CapturedAtUtc: DateTimeOffset.UtcNow);

        var result = tracker.TryRemember(
            ownWindow,
            ownProcessId: 100,
            ownWindowHandle: 10,
            isWindow: true);

        Assert.Null(result);
        Assert.Null(tracker.Current);
    }

    [Fact]
    public void TryRemember_RetainsLastValidExternalWindow()
    {
        var tracker = new ActiveWindowReferenceTracker();
        var externalWindow = new ActiveWindowReference(
            WindowHandle: 20,
            ProcessId: 200,
            ProcessName: "msedge",
            WindowTitle: "Microsoft Edge",
            CapturedAtUtc: DateTimeOffset.UtcNow);

        tracker.TryRemember(
            externalWindow,
            ownProcessId: 100,
            ownWindowHandle: 10,
            isWindow: true);
        var retained = tracker.TryRemember(
            candidate: null,
            ownProcessId: 100,
            ownWindowHandle: 10,
            isWindow: false);

        Assert.Same(externalWindow, retained);
        Assert.Same(externalWindow, tracker.Current);
    }

    [Fact]
    public void GetCurrentIfValid_ReturnsNullWhenStoredWindowIsInvalid()
    {
        var tracker = new ActiveWindowReferenceTracker();
        var externalWindow = new ActiveWindowReference(
            WindowHandle: 20,
            ProcessId: 200,
            ProcessName: "pwsh",
            WindowTitle: "",
            CapturedAtUtc: DateTimeOffset.UtcNow);

        tracker.TryRemember(
            externalWindow,
            ownProcessId: 100,
            ownWindowHandle: 10,
            isWindow: true);

        Assert.Null(tracker.GetCurrentIfValid(isWindow: false));
    }
}
