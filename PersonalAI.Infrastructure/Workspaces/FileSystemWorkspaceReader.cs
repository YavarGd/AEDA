using System.Text;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Workspaces;

public sealed class FileSystemWorkspaceReader(
    IWorkspaceRegistry registry,
    IWorkspacePathResolver resolver,
    WorkspaceToolOptions options) : IWorkspaceReader
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(
        [".git", "bin", "obj", "node_modules", ".vs", ".idea"],
        StringComparer.OrdinalIgnoreCase);

    private readonly FileTypeDetector _fileTypeDetector = CreateDetector(options);

    public WorkspaceDescriptor GetWorkspace(WorkspaceId workspaceId)
    {
        if (!registry.TryGet(workspaceId, out var workspace))
        {
            throw new WorkspaceAccessException(
                "workspace_not_found",
                "Workspace was not registered.");
        }

        return workspace;
    }

    public IReadOnlyList<WorkspaceDirectoryEntry> ListDirectory(
        WorkspaceId workspaceId,
        string relativePath,
        int maxEntries,
        bool includeHidden,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        if (maxEntries <= 0 || maxEntries > options.MaxDirectoryEntries)
        {
            throw new WorkspaceAccessException(
                "request_limit_too_high",
                "Directory entry limit was outside the allowed range.");
        }

        var path = resolver.Resolve(
            workspaceId,
            relativePath,
            WorkspacePathKind.Directory);
        var entries = new List<WorkspaceDirectoryEntry>(maxEntries + 1);

        try
        {
            foreach (var entryPath in Directory.EnumerateFileSystemEntries(path.FullPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entries.Count >= maxEntries)
                {
                    break;
                }

                try
                {
                    var attributes = File.GetAttributes(entryPath);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    var isHidden = attributes.HasFlag(FileAttributes.Hidden);
                    if (isHidden && !includeHidden)
                    {
                        continue;
                    }

                    var isDirectory = attributes.HasFlag(FileAttributes.Directory);
                    var name = Path.GetFileName(entryPath);
                    var relative = WorkspacePathResolver.NormalizeDisplayRelativePath(
                        Path.GetRelativePath(path.Workspace.CanonicalRootPath, entryPath));
                    entries.Add(new WorkspaceDirectoryEntry(
                        name,
                        relative,
                        isDirectory ? WorkspaceEntryType.Directory : WorkspaceEntryType.File,
                        isDirectory ? null : new FileInfo(entryPath).Length,
                        File.GetLastWriteTimeUtc(entryPath),
                        isHidden,
                        isDirectory ? string.Empty : Path.GetExtension(entryPath)));
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException)
                {
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            throw new WorkspaceAccessException(
                "access_denied",
                "Workspace directory could not be read.");
        }
        catch (DirectoryNotFoundException)
        {
            throw new WorkspaceAccessException(
                "directory_not_found",
                "Workspace directory was not found.");
        }

        return entries
            .OrderByDescending(entry => entry.Type == WorkspaceEntryType.Directory)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public WorkspaceTextFile ReadTextFile(
        WorkspaceId workspaceId,
        string relativePath,
        int maxCharacters,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        if (maxCharacters <= 0 || maxCharacters > options.MaxReadCharacters)
        {
            throw new WorkspaceAccessException(
                "request_limit_too_high",
                "Read character limit was outside the allowed range.");
        }

        var path = resolver.Resolve(
            workspaceId,
            relativePath,
            WorkspacePathKind.File);

        ThrowIfFinalTargetReparsePoint(path.FullPath);

        try
        {
            using var stream = new FileStream(
                path.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 8192,
                FileOptions.SequentialScan);
            var fileSizeBytes = stream.Length;
            var detection = _fileTypeDetector.Detect(stream);
            stream.Position = 0;
            var result = ReadBoundedText(
                stream,
                detection.Encoding,
                maxCharacters,
                cancellationToken);

            return new WorkspaceTextFile(
                path.RelativePath,
                result.Content,
                detection.Encoding.WebName,
                fileSizeBytes,
                result.IsTruncated,
                result.HadDecodingErrors);
        }
        catch (FileNotFoundException)
        {
            throw new WorkspaceAccessException(
                "file_not_found",
                "Workspace file was not found.");
        }
        catch (DirectoryNotFoundException)
        {
            throw new WorkspaceAccessException(
                "file_not_found",
                "Workspace file was not found.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new WorkspaceAccessException(
                "access_denied",
                "Workspace file could not be read.");
        }
        catch (IOException)
        {
            throw new WorkspaceAccessException(
                "io_error",
                "Workspace file could not be read.");
        }
    }

    private static ReadTextResult ReadBoundedText(
        Stream stream,
        Encoding encoding,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        try
        {
            return ReadBoundedTextCore(
                stream,
                encoding,
                maxCharacters,
                hadDecodingErrors: false,
                cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            var fallback = Encoding.GetEncoding(
                encoding.WebName,
                EncoderFallback.ReplacementFallback,
                DecoderFallback.ReplacementFallback);
            return ReadBoundedTextCore(
                stream,
                fallback,
                maxCharacters,
                hadDecodingErrors: true,
                cancellationToken);
        }
    }

    private static ReadTextResult ReadBoundedTextCore(
        Stream stream,
        Encoding encoding,
        int maxCharacters,
        bool hadDecodingErrors,
        CancellationToken cancellationToken)
    {
        stream.Position = 0;
        var readLimit = maxCharacters + 2;
        var content = new StringBuilder(Math.Min(maxCharacters, 8192));
        var buffer = new char[Math.Min(8192, readLimit)];
        using var reader = new StreamReader(
            stream,
            encoding,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 8192,
            leaveOpen: true);

        while (content.Length < readLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = readLimit - content.Length;
            var read = reader.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                break;
            }

            content.Append(buffer, 0, read);
        }

        var text = content.ToString();
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var isTruncated = text.Length > maxCharacters;
        if (isTruncated)
        {
            text = text[..maxCharacters];
        }

        return new ReadTextResult(text, isTruncated, hadDecodingErrors);
    }

    private static void ThrowIfFinalTargetReparsePoint(string fullPath)
    {
        try
        {
            if (File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new WorkspaceAccessException(
                    "reparse_point_rejected",
                    "Workspace path uses a reparse point.");
            }
        }
        catch (FileNotFoundException)
        {
            throw new WorkspaceAccessException(
                "file_not_found",
                "Workspace file was not found.");
        }
        catch (DirectoryNotFoundException)
        {
            throw new WorkspaceAccessException(
                "file_not_found",
                "Workspace file was not found.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new WorkspaceAccessException(
                "access_denied",
                "Workspace file could not be inspected.");
        }
        catch (IOException)
        {
            throw new WorkspaceAccessException(
                "io_error",
                "Workspace file could not be inspected.");
        }
    }

    public WorkspaceSearchResult SearchText(
        WorkspaceId workspaceId,
        string query,
        string relativeDirectory,
        string? filePattern,
        bool matchCase,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        ValidateSearchRequest(query, filePattern, maxResults);
        var root = resolver.Resolve(
            workspaceId,
            relativeDirectory,
            WorkspacePathKind.Directory);
        var matches = new List<WorkspaceSearchMatch>();
        var filesScanned = 0;
        var filesSkipped = 0;
        var fileCandidatesInspected = 0;
        var truncated = false;

        foreach (var filePath in EnumerateFiles(root.FullPath, options.MaxSearchDepth, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fileCandidatesInspected >= options.MaxSearchFiles)
            {
                truncated = true;
                break;
            }

            fileCandidatesInspected++;
            if (!MatchesPattern(filePath, filePattern))
            {
                continue;
            }

            try
            {
                var relativePath = WorkspacePathResolver.NormalizeDisplayRelativePath(
                    Path.GetRelativePath(root.Workspace.CanonicalRootPath, filePath));
                var reachedResultLimit = SearchFileText(
                    filePath,
                    relativePath,
                    query,
                    matchCase,
                    maxResults,
                    options.MaxPreviewLength,
                    matches,
                    cancellationToken);
                filesScanned++;
                if (reachedResultLimit)
                {
                    truncated = true;
                    break;
                }
            }
            catch (WorkspaceAccessException exception) when (
                IsSkippableSearchFileError(exception.SafeErrorCode))
            {
                filesSkipped++;
            }
            catch (IOException)
            {
                filesSkipped++;
            }
            catch (UnauthorizedAccessException)
            {
                filesSkipped++;
            }
        }

        return new WorkspaceSearchResult(
            query,
            root.RelativePath,
            matches,
            truncated,
            filesScanned,
            filesSkipped);
    }

    private bool SearchFileText(
        string fullPath,
        string relativePath,
        string query,
        bool matchCase,
        int maxResults,
        int maxPreviewLength,
        List<WorkspaceSearchMatch> matches,
        CancellationToken cancellationToken)
    {
        ThrowIfFinalTargetReparsePoint(fullPath);

        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 8192,
                FileOptions.SequentialScan);
            var detection = _fileTypeDetector.Detect(stream);
            stream.Position = 0;
            return SearchTextStream(
                stream,
                detection.Encoding,
                relativePath,
                query,
                matchCase,
                maxResults,
                maxPreviewLength,
                matches,
                cancellationToken);
        }
        catch (FileNotFoundException)
        {
            throw new WorkspaceAccessException(
                "file_not_found",
                "Workspace file was not found.");
        }
        catch (DirectoryNotFoundException)
        {
            throw new WorkspaceAccessException(
                "file_not_found",
                "Workspace file was not found.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new WorkspaceAccessException(
                "access_denied",
                "Workspace file could not be read.");
        }
        catch (IOException)
        {
            throw new WorkspaceAccessException(
                "io_error",
                "Workspace file could not be read.");
        }
    }

    private static bool SearchTextStream(
        Stream stream,
        Encoding encoding,
        string relativePath,
        string query,
        bool matchCase,
        int maxResults,
        int maxPreviewLength,
        List<WorkspaceSearchMatch> matches,
        CancellationToken cancellationToken)
    {
        var originalMatchCount = matches.Count;
        try
        {
            return SearchTextStreamCore(
                stream,
                encoding,
                relativePath,
                query,
                matchCase,
                maxResults,
                maxPreviewLength,
                matches,
                cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            if (matches.Count > originalMatchCount)
            {
                matches.RemoveRange(originalMatchCount, matches.Count - originalMatchCount);
            }

            var fallback = Encoding.GetEncoding(
                encoding.WebName,
                EncoderFallback.ReplacementFallback,
                DecoderFallback.ReplacementFallback);
            return SearchTextStreamCore(
                stream,
                fallback,
                relativePath,
                query,
                matchCase,
                maxResults,
                maxPreviewLength,
                matches,
                cancellationToken);
        }
    }

    private static bool SearchTextStreamCore(
        Stream stream,
        Encoding encoding,
        string relativePath,
        string query,
        bool matchCase,
        int maxResults,
        int maxPreviewLength,
        List<WorkspaceSearchMatch> matches,
        CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var reader = new StreamReader(
            stream,
            encoding,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 8192,
            leaveOpen: true);
        var comparison = matchCase
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var lineNumber = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = reader.ReadLine();
            if (line is null)
            {
                return false;
            }

            lineNumber++;
            if (lineNumber == 1 && line.Length > 0 && line[0] == '\uFEFF')
            {
                line = line[1..];
            }

            AddLineMatches(
                relativePath,
                lineNumber,
                line,
                query,
                comparison,
                maxResults,
                maxPreviewLength,
                matches);
            if (matches.Count >= maxResults)
            {
                return true;
            }
        }
    }

    private static FileTypeDetector CreateDetector(WorkspaceToolOptions options)
    {
        options.Validate();
        return new FileTypeDetector(options);
    }

    private IEnumerable<string> EnumerateFiles(
        string directory,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var stack = new Stack<(string Directory, int Depth)>();
        stack.Push((directory, 0));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, depth) = stack.Pop();
            foreach (var entry in EnumerateFileSystemEntriesSafely(current, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException)
                {
                    continue;
                }

                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (depth < maxDepth &&
                        !ExcludedDirectoryNames.Contains(Path.GetFileName(entry)))
                    {
                        stack.Push((entry, depth + 1));
                    }

                    continue;
                }

                yield return entry;
            }
        }
    }

    private static IEnumerable<string> EnumerateFileSystemEntriesSafely(
        string directory,
        CancellationToken cancellationToken)
    {
        IEnumerator<string>? enumerator;
        try
        {
            enumerator = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is DirectoryNotFoundException)
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string entry;
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        yield break;
                    }

                    entry = enumerator.Current;
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException ||
                    exception is DirectoryNotFoundException)
                {
                    yield break;
                }

                yield return entry;
            }
        }
    }

    private static void AddLineMatches(
        string relativePath,
        int lineNumber,
        string line,
        string query,
        StringComparison comparison,
        int maxResults,
        int maxPreviewLength,
        List<WorkspaceSearchMatch> matches)
    {
        var matchIndex = line.IndexOf(query, comparison);
        while (matchIndex >= 0 && matches.Count < maxResults)
        {
            var preview = CreatePreview(line, matchIndex, query.Length, maxPreviewLength);
            matches.Add(new WorkspaceSearchMatch(
                relativePath,
                lineNumber,
                preview.Text,
                preview.MatchStartIndex,
                query.Length));
            matchIndex = line.IndexOf(query, matchIndex + query.Length, comparison);
        }
    }

    private static PreviewWindow CreatePreview(
        string line,
        int lineMatchStartIndex,
        int matchLength,
        int maxPreviewLength)
    {
        if (line.Length <= maxPreviewLength)
        {
            return new PreviewWindow(line, lineMatchStartIndex);
        }

        var desiredStart = Math.Max(0, lineMatchStartIndex - (maxPreviewLength / 2));
        var maxStart = Math.Max(0, line.Length - maxPreviewLength);
        var start = Math.Min(desiredStart, maxStart);
        if (lineMatchStartIndex + matchLength > start + maxPreviewLength)
        {
            start = Math.Max(0, lineMatchStartIndex + matchLength - maxPreviewLength);
        }

        var length = Math.Min(maxPreviewLength, line.Length - start);
        return new PreviewWindow(line.Substring(start, length), lineMatchStartIndex - start);
    }

    private static bool IsSkippableSearchFileError(string safeErrorCode) =>
        safeErrorCode is "file_not_found" or
            "binary_file" or
            "file_too_large" or
            "access_denied" or
            "io_error" or
            "reparse_point_rejected";

    private void ValidateSearchRequest(
        string query,
        string? filePattern,
        int maxResults)
    {
        if (string.IsNullOrWhiteSpace(query) ||
            query.Length > options.MaxSearchQueryLength)
        {
            throw new WorkspaceAccessException(
                "invalid_search_query",
                "Search query was empty or too long.");
        }

        if (maxResults <= 0 || maxResults > options.MaxSearchResults)
        {
            throw new WorkspaceAccessException(
                "search_limit_exceeded",
                "Search result limit was outside the allowed range.");
        }

        ValidateFilePattern(filePattern);
    }

    public static void ValidateFilePattern(string? filePattern)
    {
        if (string.IsNullOrWhiteSpace(filePattern))
        {
            return;
        }

        if (filePattern.Length > 64 ||
            filePattern.Contains('/') ||
            filePattern.Contains('\\') ||
            filePattern.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(filePattern) ||
            !filePattern.StartsWith("*.", StringComparison.Ordinal) ||
            filePattern.Length <= 2 ||
            filePattern.Count(character => character == '*') != 1 ||
            filePattern[2..].Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '_' and not '-' and not '.'))
        {
            throw new WorkspaceAccessException(
                "invalid_file_pattern",
                "Search file pattern was invalid.");
        }
    }

    private static bool MatchesPattern(string filePath, string? filePattern)
    {
        if (string.IsNullOrWhiteSpace(filePattern))
        {
            return true;
        }

        return string.Equals(
            Path.GetExtension(filePath),
            filePattern[1..],
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ReadTextResult(
        string Content,
        bool IsTruncated,
        bool HadDecodingErrors);

    private sealed record PreviewWindow(string Text, int MatchStartIndex);
}
