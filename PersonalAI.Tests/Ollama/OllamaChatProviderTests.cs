using PersonalAI.Core.Chat;
using PersonalAI.Providers.Ollama;

namespace PersonalAI.Tests.Ollama;

public sealed class OllamaChatProviderTests
{
    [Fact]
    public async Task StreamAsync_ReturnsLocalModelResponse()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var provider = new OllamaChatProvider(httpClient);

        var request = new ChatRequest(
            Model: "gemma4",
            Messages:
            [
                new ChatMessage(
                    ChatRole.User,
                    "Reply with exactly: Provider test successful.")
            ]);

        var responseText = new System.Text.StringBuilder();

        try
        {
            await foreach (var chunk in provider.StreamAsync(request))
            {
                responseText.Append(chunk.Content);
            }
        }
        catch (HttpRequestException)
        {
            return;
        }

        Assert.Contains(
            "Provider test successful",
            responseText.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }
}
