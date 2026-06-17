using PersonalAI.Core.Chat;

namespace PersonalAI.Tests.Chat;

public sealed class ExplicitModelOverrideParserTests
{
    [Fact]
    public void ParseCommand_StandaloneModelSetsConversationOverride()
    {
        var result = ExplicitModelOverrideParser.ParseCommand("  /model qwen3:8b  ");

        Assert.Equal(ModelCommandKind.ConversationOverride, result.Kind);
        Assert.Equal("qwen3:8b", result.Model);
        Assert.Null(result.Prompt);
    }

    [Fact]
    public void ParseCommand_AutoClearsConversationOverride()
    {
        var result = ExplicitModelOverrideParser.ParseCommand("/MODEL auto");

        Assert.Equal(ModelCommandKind.ClearConversationOverride, result.Kind);
    }

    [Fact]
    public void ParseCommand_OneTurnSyntaxSeparatesPrompt()
    {
        var result = ExplicitModelOverrideParser.ParseCommand(
            "/model qwen3-vl:8b -- what do we see?");

        Assert.Equal(ModelCommandKind.OneTurnOverride, result.Kind);
        Assert.Equal("qwen3-vl:8b", result.Model);
        Assert.Equal("what do we see?", result.Prompt);
    }

    [Fact]
    public void ParseCommand_NewlineDirectiveIsMalformed()
    {
        var result = ExplicitModelOverrideParser.ParseCommand(
            "/model qwen3:8b\nwhat do we see?");

        Assert.Equal(ModelCommandKind.Malformed, result.Kind);
        Assert.NotNull(result.ErrorMessage);
    }

    [Theory]
    [InlineData("Please use /model qwen3:8b")]
    [InlineData("/modelish qwen3:8b")]
    public void ParseCommand_DoesNotInterpretInlineOrSimilarText(string prompt)
    {
        var result = ExplicitModelOverrideParser.ParseCommand(prompt);

        Assert.Equal(ModelCommandKind.None, result.Kind);
    }

    [Theory]
    [InlineData("/model")]
    [InlineData("/model ")]
    [InlineData("/model qwen3:8b -- ")]
    [InlineData("/model qwen 3:8b")]
    public void ParseCommand_MalformedCommandsAreExplicit(string prompt)
    {
        var result = ExplicitModelOverrideParser.ParseCommand(prompt);

        Assert.Equal(ModelCommandKind.Malformed, result.Kind);
        Assert.NotNull(result.ErrorMessage);
    }
}
