namespace PersonalAI.Desktop.WinUI.Services;

public enum ComposerKeyboardAction
{
    None,
    Send,
    InsertNewLine,
    CancelGeneration
}

public static class ComposerKeyboardInteraction
{
    public static ComposerKeyboardAction ForEnter(
        bool shiftDown,
        bool canSend,
        string prompt)
    {
        if (shiftDown)
        {
            return ComposerKeyboardAction.InsertNewLine;
        }

        return canSend && !string.IsNullOrWhiteSpace(prompt)
            ? ComposerKeyboardAction.Send
            : ComposerKeyboardAction.None;
    }

    public static ComposerKeyboardAction ForEscape(bool isGenerationActive) =>
        isGenerationActive
            ? ComposerKeyboardAction.CancelGeneration
            : ComposerKeyboardAction.None;
}
