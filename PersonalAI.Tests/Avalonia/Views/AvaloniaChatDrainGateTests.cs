using System.Runtime.CompilerServices;
using PersonalAI.Desktop.Avalonia.Views.Chat;

namespace PersonalAI.Tests.Avalonia.Views;

/// <summary>
/// New Chat and Send replace timeline content, so they must not run while a queued
/// conversation load could still publish. Cancelling the token is not sufficient: a load
/// whose reads already completed ignores it and posts anyway.
/// </summary>
public sealed class AvaloniaChatDrainGateTests
{
    [Fact]
    public async Task ReplacementActions_CannotRunUntilACancellationInsensitiveLoadDrains()
    {
        var queue = new ChatConversationLoadQueue();
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var published = false;

        // Mirrors the view: count outstanding queue tasks, allow replacement only at zero.
        var outstanding = 0;
        var replacementRan = false;

        void TryReplacementAction()
        {
            if (outstanding > 0)
            {
                return;
            }

            replacementRan = true;
        }

        // This load deliberately ignores its token, like a read that already completed.
        outstanding++;
        var load = queue.Enqueue(
            async _ =>
            {
                started.SetResult();
                await release.Task;
                published = true;
            },
            () => { });

        var tracked = TrackAsync();

        await started.Task;

        // Invalidate does not help here, so the gate must hold on its own.
        queue.Invalidate();
        TryReplacementAction();

        Assert.False(
            replacementRan,
            "New Chat / Send must be blocked while a queued load can still publish");
        Assert.False(published);

        release.SetResult();
        await tracked;

        Assert.True(published);
        Assert.Equal(0, outstanding);

        // Only after the chain has fully drained may the replacement action proceed.
        TryReplacementAction();
        Assert.True(replacementRan, "controls must be restored once all queued loads finish");

        async Task TrackAsync()
        {
            try
            {
                await load;
            }
            catch
            {
            }

            outstanding--;
        }
    }

    [Fact]
    public void NewChatAndSendPaths_AllConsultTheDrainGate()
    {
        var source = ReadChatViewSource();

        // Button click, composer Enter, shell Ctrl+N, and shell Send all gate on drain.
        Assert.Contains("private bool IsLoadChainDraining => _outstandingLoads > 0;", source);
        Assert.Contains("NewChatButton.IsEnabled = !draining;", source);
        Assert.Contains("SendButton.IsEnabled = !draining;", source);

        var newChatClick = ExtractMethod(
            source, "private void OnNewChatClick", "private void OnSendClick");
        Assert.Contains("IsLoadChainDraining", newChatClick);

        var startNewChat = ExtractMethod(
            source, "private void StartNewChat", "private static void Execute");
        Assert.Contains("IsLoadChainDraining", startNewChat);

        var composerKeyDown = ExtractMethod(
            source, "private void OnComposerKeyDown", "private void StartNewChat");
        Assert.Contains("IsLoadChainDraining", composerKeyDown);

        var applyKeyAction = ExtractMethod(
            source, "public void ApplyKeyAction", "private bool IsLoadChainDraining");
        Assert.Contains("IsLoadChainDraining", applyKeyAction);

        // Cancelling generation must never be gated.
        var cancelBranch = ExtractMethod(
            source, "case ChatKeyAction.Cancel:", "case ChatKeyAction.NewChat:");
        Assert.DoesNotContain("IsLoadChainDraining", cancelBranch);
    }

    private static string ExtractMethod(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"'{start}' not found in ChatView source");

        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        return endIndex < 0 ? source[startIndex..] : source[startIndex..endIndex];
    }

    private static string ReadChatViewSource([CallerFilePath] string testFilePath = "")
    {
        // <repo>/PersonalAI.Tests/Avalonia/Views/<this file>
        var viewsDirectory = Path.GetDirectoryName(testFilePath)!;
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(viewsDirectory, "..", "..", ".."));
        var path = Path.Combine(
            repositoryRoot,
            "PersonalAI.Desktop.Avalonia",
            "Views",
            "Chat",
            "ChatView.axaml.cs");

        Assert.True(File.Exists(path), $"ChatView source not found at {path}");
        return File.ReadAllText(path);
    }
}
