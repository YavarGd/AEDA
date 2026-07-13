using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Ui;

public sealed class WindowsUiaSelectedTextProviderTests
{
    private static readonly ActiveWindowReference Foreground = new(
        1, 42, "notepad", "notes.txt - Notepad", DateTimeOffset.UtcNow);

    [Fact]
    public async Task ValidSelectionIsTrustedAndBounded()
    {
        var provider = new WindowsUiaSelectedTextProvider((_, _) => "selected text");

        var result = await provider.TryGetSelectedTextAsync(
            Foreground, PrivacySettings.Default, 8, CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.True(result.IsTrustedForImmediateSubmission);
        Assert.Equal("selected", result.Text);
        Assert.Equal("notepad", result.ApplicationIdentity);
    }

    [Fact]
    public async Task EmptyOrUnsupportedSelectionFallsBackSafely()
    {
        var provider = new WindowsUiaSelectedTextProvider((_, _) => null);

        var result = await provider.TryGetSelectedTextAsync(
            Foreground, PrivacySettings.Default, 100, CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.False(result.IsTrustedForImmediateSubmission);
        Assert.Null(result.Text);
    }

    [Theory]
    [InlineData("1Password", "Vault")]
    [InlineData("msedge", "InPrivate - Microsoft Edge")]
    public async Task SensitiveApplicationsAreBlockedBeforeAutomation(
        string processName,
        string title)
    {
        var invoked = false;
        var provider = new WindowsUiaSelectedTextProvider((_, _) =>
        {
            invoked = true;
            return "secret";
        });

        var result = await provider.TryGetSelectedTextAsync(
            Foreground with { ProcessName = processName, WindowTitle = title },
            PrivacySettings.Default,
            100,
            CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.False(invoked);
    }

    [Fact]
    public async Task SlowAutomationTimesOutToFallback()
    {
        var provider = new WindowsUiaSelectedTextProvider((_, _) =>
        {
            Thread.Sleep(1_000);
            return "late";
        });

        var result = await provider.TryGetSelectedTextAsync(
            Foreground, PrivacySettings.Default, 100, CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Contains("timed out", result.SafeFailureReason, StringComparison.OrdinalIgnoreCase);
    }
}
