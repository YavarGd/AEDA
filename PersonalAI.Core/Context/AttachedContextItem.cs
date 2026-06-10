namespace PersonalAI.Core.Context;

using PersonalAI.Core.Chat;

public sealed record AttachedContextItem(
    Guid Id,
    AttachedContextType Type,
    string SourceName,
    string DisplayTitle,
    string Preview,
    string ProviderPayload,
    IReadOnlyList<ChatImage> Images,
    string? ThumbnailDataUri,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAtUtc,
    string DuplicateKey);
