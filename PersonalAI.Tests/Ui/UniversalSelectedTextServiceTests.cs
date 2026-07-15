using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Ui;

public sealed class UniversalSelectedTextServiceTests
{
    private static readonly ActiveWindowReference Foreground = new(
        1, 42, "notepad", "notes.txt - Notepad", DateTimeOffset.UtcNow);

    [Fact]
    public async Task ExplicitMatchingContextWinsWithoutOtherCapture()
    {
        var uia = new FakeUia("uia");
        var copy = new FakeCopy();
        var service = new UniversalSelectedTextService(uia, copy);
        var explicitContext = Context(AttachedContextType.VsCodeEditor, "Code", "Program.cs", "repo");

        var result = await service.CaptureAsync(
            Request(Foreground with
            {
                ProcessName = "Code",
                WindowTitle = "Program.cs - repo - Visual Studio Code"
            }, explicitContext),
            CancellationToken.None);

        Assert.Equal(SelectedTextCaptureSource.ExplicitIntegration, result.Source);
        Assert.Same(explicitContext, result.ExplicitContext);
        Assert.Equal(0, uia.Calls);
        Assert.Equal(0, copy.Calls);
    }

    [Fact]
    public async Task UiaSuccessDoesNotTouchClipboard()
    {
        var copy = new FakeCopy();
        var service = new UniversalSelectedTextService(new FakeUia("selected"), copy);

        var result = await service.CaptureAsync(Request(Foreground), CancellationToken.None);

        Assert.Equal(SelectedTextCaptureSource.UiAutomationTextPattern, result.Source);
        Assert.Equal("selected", result.Text);
        Assert.Equal(0, copy.Calls);
    }

    [Fact]
    public async Task UiaFailureFallsThroughToCopyWithoutMerging()
    {
        var copy = new FakeCopy { Text = "copied" };
        var service = new UniversalSelectedTextService(new FakeUia(null), copy);

        var result = await service.CaptureAsync(Request(Foreground), CancellationToken.None);

        Assert.Equal(SelectedTextCaptureSource.ClipboardCopyFallback, result.Source);
        Assert.Equal("copied", result.Text);
        Assert.Equal(1, copy.Calls);
    }

    [Theory]
    [InlineData("msedge", "Gmail body")]
    [InlineData("chrome", "Gmail body")]
    [InlineData("Obsidian", "Editor")]
    [InlineData("notepad++", "Scintilla")]
    public async Task NonUiaEditorsUseGenericFocusedControlCopy(
        string process,
        string title)
    {
        var foreground = Foreground with
        {
            ProcessName = process,
            WindowTitle = title
        };
        var copy = new FakeCopy { Text = "selected" };
        var service = new UniversalSelectedTextService(new FakeUia(null), copy);

        var result = await service.CaptureAsync(Request(foreground), CancellationToken.None);

        Assert.Equal(SelectedTextCaptureSource.ClipboardCopyFallback, result.Source);
        Assert.Equal("selected", result.Text);
        Assert.Equal(1, copy.Calls);
    }

    [Fact]
    public async Task StaleOrMismatchedExplicitContextIsRejected()
    {
        var copy = new FakeCopy { Text = "current" };
        var service = new UniversalSelectedTextService(new FakeUia(null), copy);
        var stale = Context(AttachedContextType.VsCodeEditor, "Code", "Other.cs", "elsewhere") with
        {
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-3)
        };

        var result = await service.CaptureAsync(Request(Foreground, stale), CancellationToken.None);

        Assert.Equal(SelectedTextCaptureSource.ClipboardCopyFallback, result.Source);
        Assert.NotSame(stale, result.ExplicitContext);
    }

    [Fact]
    public async Task PrivacyBlockStopsAllProviders()
    {
        var uia = new FakeUia("secret");
        var copy = new FakeCopy();
        var service = new UniversalSelectedTextService(uia, copy);

        var result = await service.CaptureAsync(
            Request(Foreground with { ProcessName = "1Password", WindowTitle = "Vault" }),
            CancellationToken.None);

        Assert.Equal(SelectedTextCaptureFailure.PrivacyBlocked, result.FailureReason);
        Assert.Equal(0, uia.Calls);
        Assert.Equal(0, copy.Calls);
    }

    private static SelectedTextCaptureRequest Request(
        ActiveWindowReference foreground,
        AttachedContextItem? explicitContext = null) => new(
            foreground, PrivacySettings.Default, 1_000, true, explicitContext);

    private static AttachedContextItem Context(
        AttachedContextType type,
        string process,
        string file,
        string workspace) => new(
            Guid.NewGuid(), type, process, file, file, "selected",
            [], null,
            new Dictionary<string, string>
            {
                ["selectedTextCharacters"] = "8",
                ["fileName"] = file,
                ["workspace"] = workspace
            },
            DateTimeOffset.UtcNow,
            Guid.NewGuid().ToString("N"));

    private sealed class FakeUia(string? text) : ISelectedTextContextProvider
    {
        public int Calls { get; private set; }

        public Task<SelectedTextContextResult> TryGetSelectedTextAsync(
            ActiveWindowReference foreground,
            PrivacySettings privacy,
            int maxCharacters,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new SelectedTextContextResult(
                text is not null, text, "UIA", foreground.ProcessName,
                DateTimeOffset.UtcNow, text is null ? "none" : null, text is not null));
        }
    }

    private sealed class FakeCopy : IClipboardCopySelectedTextProvider
    {
        public int Calls { get; private set; }
        public string? Text { get; init; }

        public Task<SelectedTextCaptureResult> CaptureAsync(
            SelectedTextCaptureRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new SelectedTextCaptureResult(
                Text is not null, Text,
                Text is null ? SelectedTextCaptureSource.None : SelectedTextCaptureSource.ClipboardCopyFallback,
                request.Foreground.ProcessName, DateTimeOffset.UtcNow,
                Text is null ? SelectedTextCaptureFailure.NoSelection : SelectedTextCaptureFailure.None,
                true, true, Text is null ? "none" : "copy"));
        }
    }
}
