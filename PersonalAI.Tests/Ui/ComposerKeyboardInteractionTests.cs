using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Ui;

public sealed class ComposerKeyboardInteractionTests
{
    [Fact]
    public void Enter_SendsWhenPromptIsValidAndCommandCanExecute()
    {
        var action = ComposerKeyboardInteraction.ForEnter(
            shiftDown: false,
            canSend: true,
            prompt: "hello");

        Assert.Equal(ComposerKeyboardAction.Send, action);
    }

    [Fact]
    public void ShiftEnter_InsertsNewLine()
    {
        var action = ComposerKeyboardInteraction.ForEnter(
            shiftDown: true,
            canSend: true,
            prompt: "hello");

        Assert.Equal(ComposerKeyboardAction.InsertNewLine, action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Enter_DoesNotSendEmptyPrompts(string prompt)
    {
        var action = ComposerKeyboardInteraction.ForEnter(
            shiftDown: false,
            canSend: true,
            prompt);

        Assert.Equal(ComposerKeyboardAction.None, action);
    }

    [Fact]
    public void Enter_RespectsCommandAvailabilityWithAttachmentsPresent()
    {
        var action = ComposerKeyboardInteraction.ForEnter(
            shiftDown: false,
            canSend: false,
            prompt: "hello");

        Assert.Equal(ComposerKeyboardAction.None, action);
    }

    [Fact]
    public void Escape_CancelsOnlyWhenGenerationIsActive()
    {
        Assert.Equal(
            ComposerKeyboardAction.CancelGeneration,
            ComposerKeyboardInteraction.ForEscape(isGenerationActive: true));
        Assert.Equal(
            ComposerKeyboardAction.None,
            ComposerKeyboardInteraction.ForEscape(isGenerationActive: false));
    }
}
