using Avalonia.Input;
using PersonalAI.Desktop.Avalonia.Views.Chat;

namespace PersonalAI.Tests.Avalonia.Views;

public sealed class AvaloniaShellKeyboardPolicyTests
{
    [Fact]
    public void Composer_Enter_Sends()
    {
        var action = ChatKeyboardPolicy.ResolveComposerKey(
            Key.Enter, KeyModifiers.None, isGenerating: false);

        Assert.Equal(ChatKeyAction.Send, action);
    }

    [Fact]
    public void Composer_ShiftEnter_InsertsNewlineInsteadOfSending()
    {
        var action = ChatKeyboardPolicy.ResolveComposerKey(
            Key.Enter, KeyModifiers.Shift, isGenerating: false);

        Assert.Equal(ChatKeyAction.InsertNewline, action);
    }

    [Fact]
    public void Escape_CancelsOnlyWhileGenerating()
    {
        Assert.Equal(
            ChatKeyAction.Cancel,
            ChatKeyboardPolicy.ResolveShellKey(Key.Escape, KeyModifiers.None, true));
        Assert.Equal(
            ChatKeyAction.None,
            ChatKeyboardPolicy.ResolveShellKey(Key.Escape, KeyModifiers.None, false));
    }

    [Fact]
    public void ControlN_StartsNewChat()
    {
        var action = ChatKeyboardPolicy.ResolveShellKey(
            Key.N, KeyModifiers.Control, isGenerating: false);

        Assert.Equal(ChatKeyAction.NewChat, action);
    }

    [Fact]
    public void Shell_Enter_IsNotTreatedAsSend()
    {
        var action = ChatKeyboardPolicy.ResolveShellKey(
            Key.Enter, KeyModifiers.None, isGenerating: false);

        Assert.Equal(ChatKeyAction.None, action);
    }

    [Fact]
    public void UnrelatedKeys_DoNothing()
    {
        Assert.Equal(
            ChatKeyAction.None,
            ChatKeyboardPolicy.ResolveComposerKey(Key.A, KeyModifiers.None, false));
        Assert.Equal(
            ChatKeyAction.None,
            ChatKeyboardPolicy.ResolveShellKey(Key.N, KeyModifiers.None, false));
    }
}
