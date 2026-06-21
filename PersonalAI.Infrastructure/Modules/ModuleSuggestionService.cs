using PersonalAI.Core.Coding;
using PersonalAI.Core.Modules;

namespace PersonalAI.Infrastructure.Modules;

public sealed class ModuleSuggestionService : IModuleSuggestionService
{
    private static readonly string[] CodeIndicators =
    [
        ".cs",
        ".csproj",
        "build",
        "class",
        "code",
        "compile",
        "controller",
        "debug",
        "diff",
        "failing test",
        "fix bug",
        "method",
        "patch",
        "refactor",
        "repository",
        "test",
        "unit test"
    ];

    private static readonly string[] NonCodeIndicators =
    [
        "browser",
        "canva",
        "image",
        "photo",
        "picture",
        "powerpoint",
        "presentation",
        "spreadsheet",
        "voice",
        "word document"
    ];

    private static readonly string[] ResearchIndicators =
    [
        "are you sure",
        "verify this",
        "fact check",
        "fact-check",
        "check this claim",
        "is this true"
    ];

    public ModuleSuggestion Suggest(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            return None();
        }

        var normalized = userText.Trim().ToLowerInvariant();
        if (ResearchIndicators.Any(normalized.Contains))
        {
            return new ModuleSuggestion(
                true,
                AedaModuleId.Research.Value,
                "Open in AEDA Research to verify with local evidence?",
                AutoLaunch: false);
        }

        if (NonCodeIndicators.Any(normalized.Contains))
        {
            return None();
        }

        var shouldSuggest = CodeIndicators.Any(normalized.Contains);
        return shouldSuggest
            ? new ModuleSuggestion(
                true,
                AedaModuleId.Code.Value,
                "This looks like a coding task. Open in AEDA Code?",
                AutoLaunch: false)
            : None();
    }

    private static ModuleSuggestion None() =>
        new(
            false,
            string.Empty,
            string.Empty,
            AutoLaunch: false);
}
