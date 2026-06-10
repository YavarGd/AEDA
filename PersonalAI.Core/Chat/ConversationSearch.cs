namespace PersonalAI.Core.Chat;

public static class ConversationSearch
{
    public static IReadOnlyList<Conversation> FilterByTitle(
        IEnumerable<Conversation> conversations,
        string? searchText)
    {
        ArgumentNullException.ThrowIfNull(conversations);

        var query = searchText?.Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            return conversations.ToArray();
        }

        return conversations
            .Where(conversation => conversation.Title.Contains(
                query,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
