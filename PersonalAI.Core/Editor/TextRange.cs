namespace PersonalAI.Core.Editor;

public sealed record TextRange(
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter);
