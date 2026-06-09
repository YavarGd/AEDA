namespace PersonalAI.Core.Context;

public interface IActiveContextProvider
{
    Task<ActiveApplicationContext?> CaptureAsync(
        ContextCaptureRequest request,
        CancellationToken cancellationToken = default);
}
