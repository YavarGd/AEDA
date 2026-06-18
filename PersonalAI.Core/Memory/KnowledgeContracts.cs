using System.Security.Cryptography;
using System.Text;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Core.Memory;

public enum KnowledgeSourceType
{
    WorkspaceFile,
    Conversation,
    TaskArtifact,
    ManualNote
}

public enum DocumentIndexState
{
    Pending,
    Indexed,
    Skipped,
    Failed
}

public sealed record KnowledgeSource(
    KnowledgeSourceType Type,
    DateTimeOffset TimestampUtc,
    WorkspaceId? WorkspaceId = null,
    string? RelativePath = null,
    Guid? ConversationId = null,
    TaskId? TaskRunId = null,
    string? ArtifactId = null);

public sealed record KnowledgeDocument(
    string Id,
    KnowledgeSource Source,
    string Title,
    string ContentHash,
    DateTimeOffset UpdatedAtUtc,
    DocumentIndexState State,
    string? SafeStatusCode = null);

public sealed record KnowledgeChunk(
    string Id,
    string DocumentId,
    int Ordinal,
    string Text,
    string ContentHash,
    KnowledgeSource Source,
    DateTimeOffset UpdatedAtUtc);

public interface IKnowledgeRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task UpsertDocumentAsync(
        KnowledgeDocument document,
        IReadOnlyList<KnowledgeChunk> chunks,
        CancellationToken cancellationToken = default);

    Task<KnowledgeDocument?> GetDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeDocument>> ListDocumentsAsync(
        string? workspaceId = null,
        int limit = 200,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeChunk>> ListChunksAsync(
        string documentId,
        int limit = 200,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeChunk>> SearchChunksAsync(
        string text,
        string? workspaceId = null,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task DeleteDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    Task ClearWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken = default);
}

public sealed record ChunkingOptions(
    int MaxChunkCharacters,
    int OverlapCharacters,
    int MaxChunks)
{
    public static ChunkingOptions Default { get; } = new(
        MaxChunkCharacters: 1200,
        OverlapCharacters: 120,
        MaxChunks: 200);
}

public sealed record ChunkingResult(
    KnowledgeDocument Document,
    IReadOnlyList<KnowledgeChunk> Chunks,
    bool IsSkipped,
    string? SafeReasonCode = null);

public static class KnowledgeChunker
{
    public const int MaxDocumentCharacters = 1_000_000;

    public static ChunkingResult ChunkText(
        string documentId,
        string title,
        string text,
        KnowledgeSource source,
        ChunkingOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        options ??= ChunkingOptions.Default;
        var maxChunkCharacters = Math.Clamp(options.MaxChunkCharacters, 100, 8000);
        var overlap = Math.Clamp(options.OverlapCharacters, 0, maxChunkCharacters / 2);
        var maxChunks = Math.Clamp(options.MaxChunks, 1, 1000);
        var now = source.TimestampUtc.ToUniversalTime();
        var contentHash = ComputeHash(text);
        var document = new KnowledgeDocument(
            documentId,
            source,
            title,
            contentHash,
            now,
            DocumentIndexState.Pending);

        if (LooksBinary(text) || text.Length > MaxDocumentCharacters)
        {
            return new ChunkingResult(
                document with { State = DocumentIndexState.Skipped, SafeStatusCode = "unsupported_or_large_document" },
                [],
                IsSkipped: true,
                "unsupported_or_large_document");
        }

        var chunks = new List<KnowledgeChunk>();
        var index = 0;
        while (index < text.Length && chunks.Count < maxChunks)
        {
            var remaining = text.Length - index;
            var length = Math.Min(maxChunkCharacters, remaining);
            var end = index + length;
            if (end < text.Length)
            {
                var boundary = text.LastIndexOfAny(['\n', '.', ' ', ';', ','], end - 1, length);
                if (boundary > index + maxChunkCharacters / 2)
                {
                    end = boundary + 1;
                    length = end - index;
                }
            }

            var chunkText = text.Substring(index, length).Trim();
            if (chunkText.Length > 0)
            {
                var ordinal = chunks.Count;
                chunks.Add(new KnowledgeChunk(
                    $"{documentId}:{ordinal:D6}",
                    documentId,
                    ordinal,
                    chunkText,
                    ComputeHash(chunkText),
                    source,
                    now));
            }

            if (end >= text.Length)
            {
                break;
            }

            index = Math.Max(end - overlap, index + 1);
        }

        return new ChunkingResult(document, chunks, IsSkipped: false);
    }

    public static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool LooksBinary(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        var sampleLength = Math.Min(text.Length, 4096);
        var controlCount = 0;
        for (var index = 0; index < sampleLength; index++)
        {
            var character = text[index];
            if (character == '\0')
            {
                return true;
            }

            if (char.IsControl(character) &&
                character is not '\r' and not '\n' and not '\t')
            {
                controlCount++;
            }
        }

        return controlCount > sampleLength / 20;
    }
}
