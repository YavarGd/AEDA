using PersonalAI.Core.Chat;
using PersonalAI.Core.Chat.Rendering;
using PersonalAI.Desktop.Avalonia.Views.Chat;
using PersonalAI.Infrastructure.Chat;

namespace PersonalAI.Tests.Avalonia.Views;

public sealed class AvaloniaChatMarkdownPresentationTests
{
    [Theory]
    [InlineData(ChatStatus.Ready)]
    [InlineData(ChatStatus.Connecting)]
    [InlineData(ChatStatus.Generating)]
    [InlineData(ChatStatus.Completed)]
    [InlineData(ChatStatus.Cancelled)]
    [InlineData(ChatStatus.Failed)]
    public void EveryStatus_HasReadableText(ChatStatus status)
    {
        // Status must never be conveyed by colour alone.
        var text = ChatPresentation.DescribeStatus(status, statusMessage: null);

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void SafeFailureMessage_TakesPrecedenceOverStatusLabel()
    {
        var text = ChatPresentation.DescribeStatus(
            ChatStatus.Failed,
            AvaloniaChatViewModelSafeText);

        Assert.Equal(AvaloniaChatViewModelSafeText, text);
    }

    [Theory]
    [InlineData(ChatRole.User, "You")]
    [InlineData(ChatRole.Assistant, "AEDA")]
    [InlineData(ChatRole.Tool, "Workspace tool")]
    [InlineData(ChatRole.System, "System")]
    public void EveryRole_HasTextLabel(ChatRole role, string expected)
    {
        Assert.Equal(expected, ChatPresentation.DescribeRole(role));
    }

    [Fact]
    public void Headings_Lists_And_Emphasis_SurviveAsAccessibleText()
    {
        var markdown = """
            # Title
            Some **bold** and *italic* text with `code`.

            - first item
            - second item
            """;

        var rendered = ChatMarkdownRenderer.Shared.Render(markdown);
        var accessible = ChatPresentation.ToAccessibleText(rendered);

        Assert.Contains("Title", accessible);
        Assert.Contains("bold", accessible);
        Assert.Contains("italic", accessible);
        Assert.Contains("code", accessible);
        Assert.Contains("first item", accessible);
        Assert.Contains("second item", accessible);
    }

    [Fact]
    public void FencedCode_IsPreservedInAccessibleText()
    {
        var markdown = """
            ```csharp
            var x = 1;
            ```
            """;

        var rendered = ChatMarkdownRenderer.Shared.Render(markdown);

        Assert.Contains("var x = 1;", ChatPresentation.ToAccessibleText(rendered));
    }

    [Fact]
    public void UnsupportedConstruct_RemainsReadableText()
    {
        // A table is not specially supported; it must still be readable.
        var markdown = "| a | b |";

        var rendered = ChatMarkdownRenderer.Shared.Render(markdown);

        Assert.Contains("a", ChatPresentation.ToAccessibleText(rendered));
        Assert.Contains("b", ChatPresentation.ToAccessibleText(rendered));
    }

    [Fact]
    public void UnsafeLink_IsDescribedWithItsTargetInsteadOfBeingDropped()
    {
        var link = new ChatLinkInline("click me", "javascript:alert(1)", IsSafe: false);

        var described = ChatPresentation.DescribeUnsafeLink(link);

        Assert.Contains("click me", described);
        Assert.Contains("javascript:alert(1)", described);
    }

    [Fact]
    public void EmptyContent_ProducesEmptyAccessibleText()
    {
        Assert.Equal(string.Empty, ChatPresentation.ToAccessibleText(null));
    }

    private const string AvaloniaChatViewModelSafeText =
        "Something went wrong. Please try again.";
}
