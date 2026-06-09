using PersonalAI.Core.Chat;
using PersonalAI.Core.Context;

namespace PersonalAI.Tests.Context;

public sealed class ContextPromptComposerTests
{
    [Fact]
    public void Compose_AddsSystemContextBeforeUserPrompt()
    {
        var context = new ActiveApplicationContext(
            100,
            200,
            "devenv",
            null,
            "Program.cs",
            "public sealed class Sample",
            null,
            null,
            DateTimeOffset.UtcNow);

        var messages = ContextPromptComposer.Compose(
            [new ChatMessage(ChatRole.Assistant, "Earlier answer")],
            "Explain this",
            context);

        Assert.Collection(
            messages,
            message => Assert.Equal(ChatRole.Assistant, message.Role),
            message =>
            {
                Assert.Equal(ChatRole.System, message.Role);
                Assert.Contains("Attached active-window context", message.Content);
                Assert.Contains("public sealed class Sample", message.Content);
            },
            message =>
            {
                Assert.Equal(ChatRole.User, message.Role);
                Assert.Equal("Explain this", message.Content);
            });
    }

    [Fact]
    public void Compose_LeavesPromptUnchangedWithoutContext()
    {
        var messages = ContextPromptComposer.Compose(
            [],
            "Hello",
            attachedContext: null);

        var message = Assert.Single(messages);
        Assert.Equal(ChatRole.User, message.Role);
        Assert.Equal("Hello", message.Content);
    }
}
