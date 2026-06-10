using PersonalAI.Core.Chat;

namespace PersonalAI.Tests.Chat;

public sealed class ChatImageTests
{
    [Fact]
    public void Constructor_RejectsMissingMediaType()
    {
        Assert.Throws<ArgumentException>(
            () => new ChatImage("", "abc"));
    }

    [Fact]
    public void Constructor_RejectsMissingImageData()
    {
        Assert.Throws<ArgumentException>(
            () => new ChatImage("image/png", " "));
    }

    [Fact]
    public void ChatMessage_TextOnlyConstructorKeepsImagesEmpty()
    {
        var message = new ChatMessage(ChatRole.User, "hello");

        Assert.Empty(message.Images);
    }
}
