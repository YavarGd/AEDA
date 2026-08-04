using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.Avalonia.Platform.Windows.Assist;
using PersonalAI.Desktop.Avalonia.Views.Assist;
using PersonalAI.Infrastructure.Context;

namespace PersonalAI.Tests.Avalonia.Assist;

public sealed class AvaloniaAssistWindowsIntegrationTests
{
    [Fact]
    public void IdleShowNeverActivatesAndPromptActivationIsIntentional()
    {
        var native = new FakeNative { ForegroundWindow = 20 };
        native.Windows[10] = (uint)Environment.ProcessId;
        native.Windows[20] = 200;
        var integration = new AvaloniaAssistWindowIntegration(() => 10, native);

        integration.CaptureFocusReturnTarget();
        Assert.True(integration.ShowIdleWithoutActivation());
        Assert.True(native.NoActivate);
        Assert.Equal(1, native.ShowNoActivateCount);
        Assert.Equal(0, native.ActivateCount);

        Assert.True(integration.ActivatePrompt());
        Assert.False(native.NoActivate);
        Assert.Equal(1, native.ActivateCount);
        Assert.True(integration.RestoreFocus());
        Assert.Equal(20, native.LastForegroundTarget);
    }

    [Fact]
    public void FocusReturnRejectsDestroyedOrReusedWindow()
    {
        var native = new FakeNative { ForegroundWindow = 20 };
        native.Windows[10] = (uint)Environment.ProcessId;
        native.Windows[20] = 200;
        var integration = new AvaloniaAssistWindowIntegration(() => 10, native);

        integration.CaptureFocusReturnTarget();
        native.Windows[20] = 201;

        Assert.False(integration.RestoreFocus());
        Assert.Equal(0, native.LastForegroundTarget);
    }

    [Theory]
    [InlineData(true, false, SelectedTextCaptureFailure.PrivacyBlocked)]
    [InlineData(false, true, SelectedTextCaptureFailure.ElevatedTarget)]
    public void ContextCaptureFailsClosedForExcludedOrElevatedTargets(
        bool excluded,
        bool elevated,
        SelectedTextCaptureFailure expected)
    {
        var target = new ActiveWindowReference(
            20,
            200,
            excluded ? "SecretApp" : "Editor",
            "Document",
            DateTimeOffset.UtcNow,
            null);
        var privacy = PrivacySettings.Default with
        {
            ExcludedApplications = excluded
                ? [new ExcludedApplicationSetting("SecretApp", null, true)]
                : []
        };

        var actual = AvaloniaAssistContextService.ValidateTarget(
            target,
            privacy,
            _ => elevated);

        Assert.Equal(expected, actual);
    }

    private sealed class FakeNative : IAvaloniaAssistWindowNative
    {
        public Dictionary<nint, uint> Windows { get; } = [];

        public nint ForegroundWindow { get; set; }

        public bool NoActivate { get; private set; }

        public int ShowNoActivateCount { get; private set; }

        public int ActivateCount { get; private set; }

        public nint LastForegroundTarget { get; private set; }

        public bool IsWindow(nint window) => Windows.ContainsKey(window);

        public uint GetProcessId(nint window) => Windows.GetValueOrDefault(window);

        public void SetNoActivate(nint window, bool enabled) => NoActivate = enabled;

        public bool ShowNoActivate(nint window)
        {
            ShowNoActivateCount++;
            return true;
        }

        public bool RestoreAndActivate(nint window)
        {
            ActivateCount++;
            return true;
        }

        public bool SetForegroundWindow(nint window)
        {
            LastForegroundTarget = window;
            return true;
        }
    }
}
