namespace PersonalAI.Core.Memory;

public interface IChatContextRetrievalService
{
    Task<RetrievalContextPack> BuildContextPackAsync(
        string userRequest,
        string? workspaceId = null,
        string? projectId = null,
        CancellationToken cancellationToken = default);
}

public sealed class ChatContextRetrievalService(IRetrievalService retrievalService)
    : IChatContextRetrievalService
{
    public Task<RetrievalContextPack> BuildContextPackAsync(
        string userRequest,
        string? workspaceId = null,
        string? projectId = null,
        CancellationToken cancellationToken = default) =>
        retrievalService.RetrieveAsync(
            new RetrievalQuery(
                userRequest,
                projectId,
                workspaceId,
                IncludeSensitive: false,
                MaxItems: 8,
                MaxCharacters: 6000),
            cancellationToken);
}
