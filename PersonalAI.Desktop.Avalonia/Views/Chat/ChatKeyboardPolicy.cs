using Avalonia.Input;

namespace PersonalAI.Desktop.Avalonia.Views.Chat;

/// <summary>
/// The keyboard action a view should perform. Kept as view-owned policy so the
/// decision is testable without instantiating controls.
/// </summary>
public enum ChatKeyAction
{
    None,
    Send,
    InsertNewline,
    Cancel,
    NewChat
}

public static class ChatKeyboardPolicy
{
    /// <summary>
    /// Keys handled while focus is inside the composer.
    /// Enter sends, Shift+Enter inserts a newline, Escape cancels only while generating.
    /// </summary>
    public static ChatKeyAction ResolveComposerKey(
        Key key,
        KeyModifiers modifiers,
        bool isGenerating)
    {
        if (key == Key.Enter)
        {
            return modifiers.HasFlag(KeyModifiers.Shift)
                ? ChatKeyAction.InsertNewline
                : ChatKeyAction.Send;
        }

        return ResolveShellKey(key, modifiers, isGenerating);
    }

    /// <summary>
    /// Keys handled at the shell level. Escape cancels only while generating so it
    /// stays predictable when the assistant is idle.
    /// </summary>
    public static ChatKeyAction ResolveShellKey(
        Key key,
        KeyModifiers modifiers,
        bool isGenerating)
    {
        if (key == Key.Escape)
        {
            return isGenerating ? ChatKeyAction.Cancel : ChatKeyAction.None;
        }

        if (key == Key.N && modifiers.HasFlag(KeyModifiers.Control))
        {
            return ChatKeyAction.NewChat;
        }

        return ChatKeyAction.None;
    }
}
