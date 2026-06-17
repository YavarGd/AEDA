namespace PersonalAI.Core.Chat;

public enum ModelCommandKind
{
    None,
    ConversationOverride,
    ClearConversationOverride,
    OneTurnOverride,
    Malformed
}

public sealed record ModelCommandParseResult(
    ModelCommandKind Kind,
    string? Model = null,
    string? Prompt = null,
    string? ErrorMessage = null);

public static class ExplicitModelOverrideParser
{
    private const string Directive = "/model";

    public static ModelCommandParseResult ParseCommand(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new ModelCommandParseResult(ModelCommandKind.None);
        }

        var trimmed = prompt.Trim();

        if (!trimmed.StartsWith(Directive, StringComparison.OrdinalIgnoreCase))
        {
            return new ModelCommandParseResult(ModelCommandKind.None);
        }

        if (trimmed.Length > Directive.Length &&
            !char.IsWhiteSpace(trimmed[Directive.Length]))
        {
            return new ModelCommandParseResult(ModelCommandKind.None);
        }

        if (trimmed.Contains('\r', StringComparison.Ordinal) ||
            trimmed.Contains('\n', StringComparison.Ordinal))
        {
            return new ModelCommandParseResult(
                ModelCommandKind.Malformed,
                ErrorMessage: "Use /model <name>, /model auto, or /model <name> -- <prompt>.");
        }

        var body = trimmed[Directive.Length..].Trim();

        if (string.IsNullOrWhiteSpace(body))
        {
            return new ModelCommandParseResult(
                ModelCommandKind.Malformed,
                ErrorMessage: "Specify a model name or auto.");
        }

        if (body.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return new ModelCommandParseResult(
                ModelCommandKind.ClearConversationOverride);
        }

        var separatorIndex = body.IndexOf(" -- ", StringComparison.Ordinal);
        var model = separatorIndex < 0
            ? body
            : body[..separatorIndex].Trim();
        var remaining = separatorIndex < 0
            ? string.Empty
            : body[(separatorIndex + 4)..].Trim();

        if (string.IsNullOrWhiteSpace(model) ||
            model.Contains(' ', StringComparison.Ordinal))
        {
            return new ModelCommandParseResult(
                ModelCommandKind.Malformed,
                ErrorMessage: "Use /model <name>, /model auto, or /model <name> -- <prompt>.");
        }

        if (separatorIndex >= 0)
        {
            if (string.IsNullOrWhiteSpace(remaining))
            {
                return new ModelCommandParseResult(
                    ModelCommandKind.Malformed,
                    ErrorMessage: "Add a prompt after -- or use standalone /model <name>.");
            }

            return new ModelCommandParseResult(
                ModelCommandKind.OneTurnOverride,
                model,
                remaining);
        }

        return new ModelCommandParseResult(
            ModelCommandKind.ConversationOverride,
            model);
    }
}
