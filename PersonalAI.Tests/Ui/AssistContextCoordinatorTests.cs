using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Ui;

public sealed class AssistContextCoordinatorTests
{
    private static readonly ActiveWindowReference Foreground = new(
        100, 42, "notepad", "notes.txt - Notepad", DateTimeOffset.UtcNow);

    [Fact]
    public async Task CaptureWithForegroundSnapshot_SensitiveAppReturnsBlocked()
    {
        var service = new FakeSelectedTextService("text");
        var coordinator = Coordinator(service);

        var envelope = await coordinator.CaptureWithForegroundSnapshotAsync(
            Foreground with { ProcessName = "1Password", WindowTitle = "Vault" },
            CancellationToken.None);

        Assert.True(envelope.IsBlocked);
        Assert.Equal("privacy-blocked", envelope.BlockedReason);
    }

    [Fact]
    public async Task CaptureWithForegroundSnapshot_ExcludedAppReturnsBlocked()
    {
        var service = new FakeSelectedTextService("text");
        var coordinator = Coordinator(service);

        var privacy = new PrivacySettings(
            [new ExcludedApplicationSetting("Bitwarden", "Bitwarden", true)],
            IncludeExecutablePathInProviderMetadata: false,
            IncludeWindowTitleInProviderContext: true);

        var coordinatorWithPrivacy = new AssistContextCoordinator(
            () => privacy,
            service);

        var envelope = await coordinatorWithPrivacy.CaptureWithForegroundSnapshotAsync(
            Foreground with { ProcessName = "Bitwarden", WindowTitle = "Vault" },
            CancellationToken.None);

        Assert.True(envelope.IsBlocked);
    }

    [Fact]
    public async Task CaptureWithForegroundSnapshot_InPrivateBrowserReturnsBlocked()
    {
        var service = new FakeSelectedTextService("text");
        var coordinator = Coordinator(service);

        var envelope = await coordinator.CaptureWithForegroundSnapshotAsync(
            Foreground with
            {
                ProcessName = "msedge",
                WindowTitle = "InPrivate browsing"
            },
            CancellationToken.None);

        Assert.True(envelope.IsBlocked);
    }

    [Fact]
    public async Task CaptureWithForegroundSnapshot_NullForegroundReturnsEmpty()
    {
        var service = new FakeSelectedTextService("text");
        var coordinator = Coordinator(service);

        var envelope = await coordinator.CaptureWithForegroundSnapshotAsync(
            null,
            CancellationToken.None);

        Assert.False(envelope.HasContext);
        Assert.Equal(AssistContextKind.None, envelope.ContextKind);
    }

    [Fact]
    public async Task CaptureWithForegroundSnapshot_UsesExistingSelectedTextProvider()
    {
        var service = new FakeSelectedTextService("selected content");
        var coordinator = Coordinator(service);

        var envelope = await coordinator.CaptureWithForegroundSnapshotAsync(
            Foreground,
            CancellationToken.None);

        Assert.True(envelope.HasContext);
        Assert.Equal("selected content", envelope.SelectedTextPreview);
        Assert.Equal(16, envelope.SelectedTextLength);
        Assert.Equal(1, service.CaptureCalls);
    }

    [Fact]
    public async Task CaptureWithForegroundSnapshot_ExplicitContextIsForwarded()
    {
        var explicitContext = new AttachedContextItem(
            Guid.NewGuid(),
            AttachedContextType.VsCodeEditor,
            "VS Code",
            "Program.cs",
            "code preview",
            "payload",
            [],
            null,
            new Dictionary<string, string>
            {
                ["selectedTextCharacters"] = "10",
                ["fileName"] = "Program.cs"
            },
            DateTimeOffset.UtcNow,
            "vscode:test");

        var service = new FakeSelectedTextService(null);
        var coordinator = Coordinator(service);

        var envelope = await coordinator.CaptureWithForegroundSnapshotAsync(
            Foreground with { ProcessName = "Code", WindowTitle = "Program.cs - repo" },
            CancellationToken.None,
            explicitContext);

        Assert.Same(explicitContext, service.LastCapturedExplicitContext);
    }

    [Fact]
    public async Task CaptureWithForegroundSnapshot_CancellationReturnsEmpty()
    {
        var service = new FakeSelectedTextService("text") { BlockUntilCancelled = true };
        var coordinator = Coordinator(service);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var envelope = await coordinator.CaptureWithForegroundSnapshotAsync(
            Foreground,
            cts.Token);

        Assert.False(envelope.HasContext);
    }

    [Fact]
    public async Task CaptureWithForegroundSnapshot_TimeoutReturnsEmpty()
    {
        var service = new FakeSelectedTextService("text") { Delay = TimeSpan.FromSeconds(5) };
        var coordinator = Coordinator(service, captureTimeout: TimeSpan.FromMilliseconds(50));

        var envelope = await coordinator.CaptureWithForegroundSnapshotAsync(
            Foreground,
            CancellationToken.None);

        Assert.False(envelope.HasContext);
    }

    [Fact]
    public async Task CaptureWithForegroundSnapshot_NoSelectionReturnsNoContext()
    {
        var service = new FakeSelectedTextService(null);
        var coordinator = Coordinator(service);

        var envelope = await coordinator.CaptureWithForegroundSnapshotAsync(
            Foreground,
            CancellationToken.None);

        Assert.False(envelope.HasContext);
        Assert.Equal(AssistContextKind.None, envelope.ContextKind);
    }

    [Fact]
    public void BuildEnvelopeFromItem_ProducesEnvelopeFromContextItem()
    {
        var item = new AttachedContextItem(
            Guid.NewGuid(),
            AttachedContextType.ApplicationWindow,
            "notepad",
            "notes.txt",
            "preview text",
            "payload",
            [],
            null,
            new Dictionary<string, string>
            {
                ["selectedTextCharacters"] = "42"
            },
            DateTimeOffset.UtcNow,
            "app:test");

        var envelope = AssistContextCoordinator.BuildEnvelopeFromItem(item, Foreground);

        Assert.True(envelope.HasContext);
        Assert.Equal("notepad", envelope.ApplicationLabel);
        Assert.Equal("notepad", envelope.ProcessIdentity);
        Assert.Equal("notes.txt - Notepad", envelope.AllowedTitle);
        Assert.Equal(AssistContextKind.ApplicationWindow, envelope.ContextKind);
        Assert.Equal(42, envelope.SelectedTextLength);
        Assert.Same(item, envelope.UnderlyingItem);
    }

    [Fact]
    public void BuildEnvelopeFromItem_NullItemReturnsEmpty()
    {
        var envelope = AssistContextCoordinator.BuildEnvelopeFromItem(null, Foreground);
        Assert.False(envelope.HasContext);
    }

    [Fact]
    public void BuildEnvelopeFromItem_NullForegroundReturnsEmpty()
    {
        var item = new AttachedContextItem(
            Guid.NewGuid(),
            AttachedContextType.ApplicationWindow,
            "notepad",
            "notes.txt",
            "preview",
            "payload",
            [],
            null,
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            "test");

        var envelope = AssistContextCoordinator.BuildEnvelopeFromItem(item, null);
        Assert.False(envelope.HasContext);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("-1")]
    public void BuildEnvelopeFromItem_NegativeSelectedTextCharacters_LeavesLengthAtZero(string negative)
    {
        var item = new AttachedContextItem(
            Guid.NewGuid(),
            AttachedContextType.ApplicationWindow,
            "notepad",
            "notes.txt",
            "preview text",
            "payload",
            [],
            null,
            new Dictionary<string, string>
            {
                ["selectedTextCharacters"] = negative
            },
            DateTimeOffset.UtcNow,
            "app:test");

        var envelope = AssistContextCoordinator.BuildEnvelopeFromItem(item, Foreground);

        Assert.Equal(0, envelope.SelectedTextLength);
    }

    [Fact]
    public void BuildEnvelopeFromItem_ZeroSelectedTextCharacters_LeavesLengthAtZero()
    {
        var item = new AttachedContextItem(
            Guid.NewGuid(),
            AttachedContextType.ApplicationWindow,
            "notepad",
            "notes.txt",
            "preview text",
            "payload",
            [],
            null,
            new Dictionary<string, string>
            {
                ["selectedTextCharacters"] = "0"
            },
            DateTimeOffset.UtcNow,
            "app:test");

        var envelope = AssistContextCoordinator.BuildEnvelopeFromItem(item, Foreground);

        Assert.Equal(0, envelope.SelectedTextLength);
    }

    [Fact]
    public void BuildEnvelopeFromItem_MissingSelectedTextCharacters_LeavesLengthAtZero()
    {
        var item = new AttachedContextItem(
            Guid.NewGuid(),
            AttachedContextType.ApplicationWindow,
            "notepad",
            "notes.txt",
            "preview text",
            "payload",
            [],
            null,
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            "app:test");

        var envelope = AssistContextCoordinator.BuildEnvelopeFromItem(item, Foreground);

        Assert.Equal(0, envelope.SelectedTextLength);
    }

    [Fact]
    public void BuildEnvelopeFromItem_UnparsableSelectedTextCharacters_LeavesLengthAtZero()
    {
        var item = new AttachedContextItem(
            Guid.NewGuid(),
            AttachedContextType.ApplicationWindow,
            "notepad",
            "notes.txt",
            "preview text",
            "payload",
            [],
            null,
            new Dictionary<string, string>
            {
                ["selectedTextCharacters"] = "not-a-number"
            },
            DateTimeOffset.UtcNow,
            "app:test");

        var envelope = AssistContextCoordinator.BuildEnvelopeFromItem(item, Foreground);

        Assert.Equal(0, envelope.SelectedTextLength);
    }

    [Fact]
    public void BuildEnvelopeFromItem_PreservesWindowTitleFromForeground()
    {
        var foreground = new ActiveWindowReference(
            200, 84, "notepad", "notes.txt - Notepad", DateTimeOffset.UtcNow);
        var item = new AttachedContextItem(
            Guid.NewGuid(),
            AttachedContextType.ApplicationWindow,
            "notepad",
            "notes.txt",
            "preview",
            "payload",
            [],
            null,
            new Dictionary<string, string>
            {
                ["selectedTextCharacters"] = "42"
            },
            DateTimeOffset.UtcNow,
            "app:test");

        var envelope = AssistContextCoordinator.BuildEnvelopeFromItem(item, foreground);

        Assert.True(envelope.HasContext);
        Assert.Equal("notes.txt - Notepad", envelope.AllowedTitle);
        Assert.Equal("notepad", envelope.ProcessIdentity);
    }

    private static AssistContextCoordinator Coordinator(
        FakeSelectedTextService service,
        TimeSpan? captureTimeout = null) =>
        new(
            () => PrivacySettings.Default,
            service,
            captureTimeout: captureTimeout);

    private sealed class FakeSelectedTextService(string? text) : IUniversalSelectedTextService
    {
        public int CaptureCalls { get; private set; }
        public AttachedContextItem? LastCapturedExplicitContext { get; private set; }
        public bool BlockUntilCancelled { get; init; }
        public TimeSpan? Delay { get; init; }

        public async Task<SelectedTextCaptureResult> CaptureAsync(
            SelectedTextCaptureRequest request,
            CancellationToken cancellationToken)
        {
            CaptureCalls++;
            LastCapturedExplicitContext = request.ExplicitContext;

            if (BlockUntilCancelled)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return new SelectedTextCaptureResult(
                        false, null, SelectedTextCaptureSource.None,
                        request.Foreground.ProcessName, DateTimeOffset.UtcNow,
                        SelectedTextCaptureFailure.Cancelled, false, true, "cancelled");
                }
            }

            if (Delay.HasValue)
            {
                await Task.Delay(Delay.Value, cancellationToken);
            }

            var source = text is not null
                ? SelectedTextCaptureSource.ClipboardCopyFallback
                : SelectedTextCaptureSource.None;

            return new SelectedTextCaptureResult(
                text is not null,
                text,
                source,
                request.Foreground.ProcessName,
                DateTimeOffset.UtcNow,
                text is null ? SelectedTextCaptureFailure.NoSelection : SelectedTextCaptureFailure.None,
                false,
                true,
                text is null ? "no-selection" : "test",
                request.ExplicitContext);
        }
    }
}
