using PersonalAI.Core.Chat;

namespace PersonalAI.Tests.Chat;

public sealed class ConversationTitleGeneratorTests
{
    [Fact]
    public void CreateTitle_ReplacesLineBreaks()
    {
        var title = ConversationTitleGenerator.CreateTitle(
            "First line\r\nSecond line\nThird line");

        Assert.Equal("First line Second line Third line", title);
    }

    [Fact]
    public void CreateTitle_TruncatesToSixtyCharactersWithEllipsis()
    {
        var title = ConversationTitleGenerator.CreateTitle(new string('a', 80));

        Assert.Equal(ConversationTitleGenerator.MaxTitleLength, title.Length);
        Assert.EndsWith("...", title);
    }

    [Theory]
    [InlineData("Explain dependency injection in one sentence.", "Explain dependency injection")]
    [InlineData("Find all references to WorkspaceRegistrationService and explain where it is used.", "Find all references to WorkspaceRegistrationService")]
    [InlineData("Can you help me fix this build error?", "Fix this build error")]
    [InlineData("# Fix this bug", "Fix this bug")]
    [InlineData("- Review the retry behavior", "Review the retry behavior")]
    public void CreateTitle_DerivesConciseTitleFromOpeningRequest(
        string prompt,
        string expected)
    {
        Assert.Equal(expected, ConversationTitleGenerator.CreateTitle(prompt));
    }

    [Fact]
    public void CreatePreview_NormalizesFirstRequestWithoutModelMetadata()
    {
        var preview = ConversationTitleGenerator.CreatePreview(
            "Find references\r\nand summarize.");

        Assert.Equal("Find references and summarize.", preview);
        Assert.DoesNotContain("Completed", preview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("qwen", preview, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateTitle_EmptyPromptFallsBackSafely()
    {
        Assert.Equal("New chat", ConversationTitleGenerator.CreateTitle("   "));
    }
}
