using PersonalAI.Core.Context;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Ui;

public sealed class AssistFocusRestorationPolicyTests
{
    [Fact]
    public void DismissWithForeground RequestsRestoration()
    {
        var foreground = new ActiveWindowReference(
            100, 42, "notepad", "notes.txt", DateTimeOffset.UtcNow);

        var request = AssistFocusRestorationPolicy.CreateRequest(
            foreground, FocusRestorationTrigger.Dismiss);

        Assert.True(request.ShouldRestore);
        Assert.Same(foreground, request.PreviousForeground);
        Assert.Equal(FocusRestorationTrigger.Dismiss, request.Trigger);
    }

    [Fact]
    public void CopyResponseWithForeground RequestsRestoration()
    {
        var foreground = new ActiveWindowReference(
            100, 42, "notepad", "notes.txt", DateTimeOffset.UtcNow);

        var request = AssistFocusRestorationPolicy.CreateRequest(
            foreground, FocusRestorationTrigger.CopyResponse);

        Assert.True(request.ShouldRestore);
        Assert.Same(foreground, request.PreviousForeground);
    }

    [Fact]
    public void ModuleOpen_DoesNotRequestRestoration()
    {
        var foreground = new ActiveWindowReference(
            100, 42, "notepad", "notes.txt", DateTimeOffset.UtcNow);

        var request = AssistFocusRestorationPolicy.CreateRequest(
            foreground, FocusRestorationTrigger.ModuleOpen);

        Assert.False(request.ShouldRestore);
    }

    [Fact]
    public void AppOpen_DoesNotRequestRestoration()
    {
        var foreground = new ActiveWindowReference(
            100, 42, "notepad", "notes.txt", DateTimeOffset.UtcNow);

        var request = AssistFocusRestorationPolicy.CreateRequest(
            foreground, FocusRestorationTrigger.AppOpen);

        Assert.False(request.ShouldRestore);
    }

    [Fact]
    public void DismissWithoutForeground_DoesNotRequestRestoration()
    {
        var request = AssistFocusRestorationPolicy.CreateRequest(
            null, FocusRestorationTrigger.Dismiss);

        Assert.False(request.ShouldRestore);
        Assert.Null(request.PreviousForeground);
    }

    [Fact]
    public void CopyResponseWithoutForeground_DoesNotRequestRestoration()
    {
        var request = AssistFocusRestorationPolicy.CreateRequest(
            null, FocusRestorationTrigger.CopyResponse);

        Assert.False(request.ShouldRestore);
    }

    [Fact]
    public void NoRestoreTriggers_PreservePreviousForegroundReference()
    {
        var foreground = new ActiveWindowReference(
            200, 84, "browser", "mail", DateTimeOffset.UtcNow);

        var moduleOpen = AssistFocusRestorationPolicy.CreateRequest(
            foreground, FocusRestorationTrigger.ModuleOpen);
        var appOpen = AssistFocusRestorationPolicy.CreateRequest(
            foreground, FocusRestorationTrigger.AppOpen);

        Assert.False(moduleOpen.ShouldRestore);
        Assert.Same(foreground, moduleOpen.PreviousForeground);
        Assert.False(appOpen.ShouldRestore);
        Assert.Same(foreground, appOpen.PreviousForeground);
    }

    [Theory]
    [InlineData(FocusRestorationTrigger.ModuleOpen)]
    [InlineData(FocusRestorationTrigger.AppOpen)]
    public void NoRestoreTriggers_AlwaysReturnFalse(
        FocusRestorationTrigger trigger)
    {
        var foreground = new ActiveWindowReference(
            100, 42, "notepad", "notes.txt", DateTimeOffset.UtcNow);

        var request = AssistFocusRestorationPolicy.CreateRequest(foreground, trigger);

        Assert.False(request.ShouldRestore);
        Assert.Equal(trigger, request.Trigger);
    }
}
