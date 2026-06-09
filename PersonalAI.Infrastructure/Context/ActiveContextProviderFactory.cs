using PersonalAI.Core.Context;

namespace PersonalAI.Infrastructure.Context;

public static class ActiveContextProviderFactory
{
    public static IActiveContextProvider CreateDefaultProvider()
    {
#if WINDOWS
        return new WindowsActiveContextProvider(new TemporaryScreenshotStore());
#else
        return new UnavailableActiveContextProvider();
#endif
    }

    private sealed class UnavailableActiveContextProvider : IActiveContextProvider
    {
        public Task<ActiveApplicationContext?> CaptureAsync(
            ContextCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ActiveApplicationContext?>(null);
        }
    }
}
