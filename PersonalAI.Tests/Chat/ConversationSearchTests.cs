using PersonalAI.Core.Chat;

namespace PersonalAI.Tests.Chat;

public sealed class ConversationSearchTests
{
    [Fact]
    public void FilterByTitle_ReturnsAllConversationsForBlankSearch()
    {
        var conversations = new[]
        {
            CreateConversation("Project notes"),
            CreateConversation("Personal tasks")
        };

        var filtered = ConversationSearch.FilterByTitle(conversations, " ");

        Assert.Equal(conversations, filtered);
    }

    [Fact]
    public void FilterByTitle_FiltersCaseInsensitiveWithoutChangingSourceCollection()
    {
        var conversations = new[]
        {
            CreateConversation("Project notes"),
            CreateConversation("Personal tasks"),
            CreateConversation("Recipe ideas")
        };

        var filtered = ConversationSearch.FilterByTitle(conversations, "pro");

        var conversation = Assert.Single(filtered);
        Assert.Equal("Project notes", conversation.Title);
        Assert.Equal(3, conversations.Length);
    }

    private static Conversation CreateConversation(string title)
    {
        var now = DateTimeOffset.UtcNow;

        return new Conversation(
            Guid.NewGuid(),
            title,
            "gemma4",
            now,
            now,
            ConversationStatus.Completed);
    }
}
