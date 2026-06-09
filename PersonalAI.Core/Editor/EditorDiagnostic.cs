namespace PersonalAI.Core.Editor;

public sealed record EditorDiagnostic(
    string Message,
    string Severity,
    TextRange? Range,
    string? Source,
    string? Code);
