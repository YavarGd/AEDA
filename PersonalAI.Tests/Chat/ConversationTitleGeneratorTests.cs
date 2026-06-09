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
}
