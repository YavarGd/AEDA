using System.Security.Cryptography;
using System.Text;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Coding;

public sealed class CodeContextService(IWorkspaceReader reader) : ICodeContextService
{
    private static readonly HashSet<string> UnsupportedExtensions = new(
        [".png", ".jpg", ".jpeg", ".gif", ".ico", ".dll", ".exe", ".zip", ".pdf"],
        StringComparer.OrdinalIgnoreCase);

    public Task<CodeContextPack> LoadFilesAsync(
        WorkspaceId workspaceId,
        IReadOnlyList<string> relativePaths,
        int maxFiles = 20,
        int maxCharactersPerFile = 100_000,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = reader.GetWorkspace(workspaceId);
        var boundedMaxFiles = Math.Clamp(maxFiles, 1, 100);
        var files = new List<CodeContextFile>();
        var skipped = new List<string>();

        foreach (var relativePath in relativePaths.Take(boundedMaxFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (UnsupportedExtensions.Contains(Path.GetExtension(relativePath)))
            {
                skipped.Add("unsupported_file_type");
                continue;
            }

            try
            {
                var text = reader.ReadTextFile(
                    workspaceId,
                    relativePath,
                    Math.Clamp(maxCharactersPerFile, 1, 500_000),
                    cancellationToken);
                if (text.HadDecodingErrors)
                {
                    skipped.Add("binary_or_decoding_error");
                    continue;
                }

                files.Add(new CodeContextFile(
                    workspaceId,
                    text.RelativePath,
                    text.Content,
                    ComputeHash(text.Content),
                    text.EncodingName,
                    text.FileSizeBytes,
                    text.IsTruncated,
                    text.HadDecodingErrors));
            }
            catch (WorkspaceAccessException exception)
            {
                skipped.Add(exception.SafeErrorCode);
            }
        }

        return Task.FromResult(new CodeContextPack(
            workspaceId,
            files,
            [],
            skipped.Distinct(StringComparer.Ordinal).ToArray(),
            relativePaths.Count > boundedMaxFiles));
    }

    public Task<CodeContextPack> SearchAsync(
        CodeContextSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = reader.GetWorkspace(request.WorkspaceId);
        var result = reader.SearchText(
            request.WorkspaceId,
            request.Query,
            request.RelativeDirectory,
            request.FilePattern,
            matchCase: false,
            Math.Clamp(request.MaxResults, 1, 100),
            cancellationToken);

        return Task.FromResult(new CodeContextPack(
            request.WorkspaceId,
            [],
            result.Matches.Select(match => new CodeContextSearchMatch(
                match.RelativeFilePath,
                match.LineNumber,
                match.LinePreview)).ToArray(),
            result.FilesSkipped > 0 ? ["search_files_skipped"] : [],
            result.IsTruncated));
    }

    public static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
