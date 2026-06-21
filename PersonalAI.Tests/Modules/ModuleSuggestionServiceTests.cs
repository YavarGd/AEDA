using PersonalAI.Core.Modules;
using PersonalAI.Infrastructure.Modules;

namespace PersonalAI.Tests.Modules;

public sealed class ModuleSuggestionServiceTests
{
    [Theory]
    [InlineData("Fix the failing test in PersonalAI.Core")]
    [InlineData("Refactor this C# class and prepare a patch")]
    [InlineData("The .csproj does not build")]
    public void Suggest_ReturnsCodeSuggestionForCodingRequests(string text)
    {
        var service = new ModuleSuggestionService();

        var suggestion = service.Suggest(text);

        Assert.True(suggestion.ShouldSuggest);
        Assert.Equal(AedaModuleId.Code.Value, suggestion.ModuleId);
        Assert.False(suggestion.AutoLaunch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Create an image for the hero")]
    [InlineData("Make a PowerPoint presentation")]
    [InlineData("Use the browser to inspect the page")]
    public void Suggest_DoesNotSuggestCodeForEmptyOrNonCodeRequests(string text)
    {
        var service = new ModuleSuggestionService();

        var suggestion = service.Suggest(text);

        Assert.False(suggestion.ShouldSuggest);
        Assert.False(suggestion.AutoLaunch);
    }
}
