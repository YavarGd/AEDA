namespace PersonalAI.Core.Editor;

public sealed record EditorContext(
    string? SelectedText,
    string? FullActiveFilePath,
    string? RelativeWorkspacePath,
    string? FileName,
    string? LanguageId,
    TextRange? Selection,
    string? WorkspaceFolderName,
    string? WorkspaceFolderPath,
    int? DocumentVersion,
    bool IsDirty,
    IReadOnlyList<EditorDiagnostic> Diagnostics,
    DateTimeOffset TimestampUtc);
