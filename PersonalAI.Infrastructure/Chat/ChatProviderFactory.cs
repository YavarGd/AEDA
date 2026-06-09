using PersonalAI.Core.Chat;
using PersonalAI.Providers.Ollama;

namespace PersonalAI.Infrastructure.Chat;

public static class ChatProviderFactory
{
    public static IChatProvider CreateDefaultLocalProvider()
    {
        return new OllamaChatProvider(new HttpClient());
    }
}
