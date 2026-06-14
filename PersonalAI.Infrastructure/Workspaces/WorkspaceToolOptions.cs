namespace PersonalAI.Infrastructure.Workspaces;

public sealed record WorkspaceToolOptions
{
    public int DefaultDirectoryEntries { get; init; } = 200;
    public int MaxDirectoryEntries { get; init; } = 1000;
    public long MaxReadableFileBytes { get; init; } = 5 * 1024 * 1024;
    public int MaxReadCharacters { get; init; } = 500_000;
    public int DefaultSearchResults { get; init; } = 100;
    public int MaxSearchResults { get; init; } = 500;
    public int MaxSearchFiles { get; init; } = 10_000;
    public int MaxSearchDepth { get; init; } = 32;
    public int MaxSearchQueryLength { get; init; } = 512;
    public int MaxPreviewLength { get; init; } = 300;

    public void Validate()
    {
        if (DefaultDirectoryEntries <= 0 ||
            MaxDirectoryEntries < DefaultDirectoryEntries ||
            MaxReadableFileBytes <= 0 ||
            MaxReadCharacters <= 0 ||
            DefaultSearchResults <= 0 ||
            MaxSearchResults < DefaultSearchResults ||
            MaxSearchFiles <= 0 ||
            MaxSearchDepth <= 0 ||
            MaxSearchQueryLength <= 0 ||
            MaxPreviewLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WorkspaceToolOptions),
                "Workspace tool limits must be positive and internally consistent.");
        }
    }
}
