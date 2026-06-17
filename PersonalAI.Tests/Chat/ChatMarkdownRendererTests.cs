using PersonalAI.Core.Chat.Rendering;

namespace PersonalAI.Tests.Chat;

public sealed class ChatMarkdownRendererTests
{
    private readonly ChatMarkdownRenderer _renderer = ChatMarkdownRenderer.Shared;

    [Fact]
    public void Render_ParsesCommonMarkdownBlocks()
    {
        var content = _renderer.Render(
            """
            # Heading

            Hello **bold** and *italic* with `code`.

            - one
              - nested

            > quote

            ---
            """);

        Assert.Contains(content.Blocks, block => block is ChatHeadingBlock);
        Assert.Contains(content.Blocks, block => block is ChatParagraphBlock);
        Assert.Contains(content.Blocks, block => block is ChatListBlock);
        Assert.Contains(content.Blocks, block => block is ChatQuoteBlock);
        Assert.Contains(content.Blocks, block => block is ChatHorizontalRuleBlock);
        Assert.Contains("Heading", content.PlainText);
    }

    [Fact]
    public void Render_ParsesFencedCodeBlocksWithoutFenceText()
    {
        var content = _renderer.Render(
            """
            ```csharp
            var value = 1;
            ```

            ```unknown-lang
            text
            ```
            """);

        var codeBlocks = content.Blocks.OfType<ChatCodeBlock>().ToArray();
        Assert.Collection(
            codeBlocks,
            first =>
            {
                Assert.Equal("csharp", first.Language);
                Assert.Equal("var value = 1;", first.Code);
            },
            second =>
            {
                Assert.Equal("unknown-lang", second.Language);
                Assert.Equal("text", second.Code);
            });
        Assert.DoesNotContain("```", content.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_IncompleteCodeFenceRemainsReadable()
    {
        var content = _renderer.Render(
            """
            ```json
            {"ok": true}
            """);

        var code = Assert.IsType<ChatCodeBlock>(Assert.Single(content.Blocks));
        Assert.Equal("json", code.Language);
        Assert.Equal("{\"ok\": true}", code.Code);
    }

    [Fact]
    public void Render_RawHtmlIsPlainText()
    {
        var content = _renderer.Render("<script>alert(1)</script>");

        var paragraph = Assert.IsType<ChatParagraphBlock>(Assert.Single(content.Blocks));
        Assert.Equal("<script>alert(1)</script>", Assert.Single(paragraph.Inlines).Text);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("javascript:alert", false)]
    [InlineData("file:///C:/secret.txt", false)]
    [InlineData("data:text/plain,hi", false)]
    [InlineData("shell:open", false)]
    [InlineData("personalai:command", false)]
    public void Render_ClassifiesSafeAndUnsafeLinks(string uri, bool isSafe)
    {
        var content = _renderer.Render($"[link]({uri})");
        var paragraph = Assert.IsType<ChatParagraphBlock>(Assert.Single(content.Blocks));
        var link = Assert.IsType<ChatLinkInline>(Assert.Single(paragraph.Inlines));

        Assert.Equal(isSafe, link.IsSafe);
    }

    [Fact]
    public void Render_VeryLongUnbrokenTextStaysPlain()
    {
        var text = new string('a', 4096);
        var content = _renderer.Render(text);

        Assert.Equal(text, content.PlainText);
    }
}
