namespace PersonalAI.Core.Context;

public sealed record AttachedContextItem(
    Guid Id,
    AttachedContextType Type,
    string SourceName,
    string DisplayTitle,
    string Preview,
    string ProviderPayload,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAtUtc,
    string DuplicateKey);
