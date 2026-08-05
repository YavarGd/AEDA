using System.Runtime.CompilerServices;

namespace PersonalAI.Tests.Avalonia.Assist;

/// <summary>
/// "Open in AEDA" must load the generated conversation AND surface the shell. The original
/// defect loaded the conversation only, so with the main window already visible on Dashboard
/// nothing appeared to happen and the external app kept focus.
/// </summary>
public sealed class AvaloniaOpenInAedaTests
{
    [Fact]
    public void HostCallbackOpensTheConversationAndThenSurfacesTheShell()
    {
        var source = ReadSource("Composition", "AvaloniaAppComposition.cs");

        // The Assist host is wired to the dedicated method, not to a bare
        // Chat.OpenConversationAsync callback.
        Assert.Contains("OpenAssistConversationInShellAsync);", source);
        Assert.DoesNotContain("? Chat.OpenConversationAsync(id)", source);

        // Order: load the conversation first, then surface it.
        var body = Between(
            source,
            "private async Task OpenAssistConversationInShellAsync",
            "Screens =");
        var open = body.IndexOf("await Chat.OpenConversationAsync(id);", StringComparison.Ordinal);
        var surface = body.IndexOf("SurfaceAssistConversationInShell?.Invoke(id);", StringComparison.Ordinal);
        Assert.True(open >= 0, "conversation load missing");
        Assert.True(surface >= 0, "shell surfacing missing");
        Assert.True(open < surface, "the conversation must be loaded before the shell is surfaced");
    }

    [Fact]
    public void NullConversationIdIsHandledSafely()
    {
        var source = ReadSource("Composition", "AvaloniaAppComposition.cs");
        var body = Between(
            source,
            "private async Task OpenAssistConversationInShellAsync",
            "Screens =");

        // No id means nothing was generated: load nothing, surface nothing, never throw.
        Assert.Contains("if (conversationId is not { } id)", body);
        Assert.Contains("return;", body);
    }

    [Fact]
    public void AppRootRequestsActivationAndRoutesToGeneralChat()
    {
        var app = ReadSource("App.axaml.cs");

        Assert.Contains("_composition.SurfaceAssistConversationInShell = SurfaceAssistConversation;", app);

        var handler = Between(app, "private void SurfaceAssistConversation", "private void ShowMainWindow");
        Assert.Contains("ShowMainWindow();", handler);
        Assert.Contains("_mainWindow?.OpenChat(newChat: false);", handler);
    }

    [Fact]
    public void ActivationIsDeferredUntilAfterTheAssistHiddenTransition()
    {
        var app = ReadSource("App.axaml.cs");
        var handler = Between(app, "private void SurfaceAssistConversation", "private void ShowMainWindow");

        // Assist hides *after* the host callback returns, and hiding restores focus to the
        // previously foreground app. Activating inline would be undone; Background priority
        // runs after that transition.
        Assert.Contains("Dispatcher.UIThread.Post(", handler);
        Assert.Contains("DispatcherPriority.Background", handler);
    }

    [Fact]
    public void OrdinaryConversationSelectionDoesNotActivateOrRerouteTheWindow()
    {
        var app = ReadSource("App.axaml.cs");

        // The previous broad rule reacted to every ActiveConversation change. Surfacing must
        // be specific to Open in AEDA so ordinary selection never steals focus.
        Assert.DoesNotContain("OnChatPropertyChanged", app);
        Assert.DoesNotContain("nameof(AvaloniaChatViewModel.ActiveConversation)", app);
        Assert.DoesNotContain("Chat.PropertyChanged", app);
    }

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"'{start}' not found");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        return endIndex < 0 ? source[startIndex..] : source[startIndex..endIndex];
    }

    private static string ReadSource(params string[] relativePath)
    {
        var path = Path.Combine(
            [GetRepositoryRoot(), "PersonalAI.Desktop.Avalonia", .. relativePath]);
        Assert.True(File.Exists(path), $"Source not found at {path}");
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot([CallerFilePath] string testFilePath = "")
    {
        // <repo>/PersonalAI.Tests/Avalonia/Assist/<this file>
        var assistDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(assistDirectory, "..", "..", ".."));
    }
}
