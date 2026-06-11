namespace PersonalAI.Core.Chat;

public sealed record ExplicitModelOverride(
    string Model,
    string PromptWithoutDirective);

public static class ExplicitModelOverrideParser
{
    private const string Directive = "/model ";

    public static ExplicitModelOverride? Parse(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        var trimmedStart = prompt.TrimStart();

        if (!trimmedStart.StartsWith(Directive, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var firstLineEnd = trimmedStart.IndexOfAny(['\r', '\n']);
        var firstLine = firstLineEnd < 0
            ? trimmedStart
            : trimmedStart[..firstLineEnd];
        var model = firstLine[Directive.Length..].Trim();

        if (string.IsNullOrWhiteSpace(model) ||
            model.Contains(' ', StringComparison.Ordinal))
        {
            return null;
        }

        var remaining = firstLineEnd < 0
            ? string.Empty
            : trimmedStart[firstLineEnd..].TrimStart('\r', '\n');

        return new ExplicitModelOverride(model, remaining);
    }
}
