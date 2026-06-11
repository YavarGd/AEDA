using PersonalAI.Core.Settings;

namespace PersonalAI.Core.Chat;

public sealed class DeterministicChatModelRouter : IChatModelRouter
{
    public Task<ModelRoutingDecision> SelectModelAsync(
        ModelRoutingRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var installed = request.InstalledModels
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hasImage = request.AttachedContexts.Any(context => context.HasImage);
        var hasEditorContext = request.AttachedContexts.Any(context =>
            context.Type.Equals("VsCodeEditor", StringComparison.OrdinalIgnoreCase));
        var overrideDirective = ExplicitModelOverrideParser.Parse(request.UserPrompt);
        var routedPrompt = overrideDirective?.PromptWithoutDirective ?? request.UserPrompt;

        if (overrideDirective is not null)
        {
            var knownModel = FindInstalled(installed, overrideDirective.Model);

            if (knownModel is null)
            {
                return Task.FromResult(ChooseCategory(
                    request,
                    installed,
                    hasImage,
                    hasEditorContext,
                    $"Requested model '{overrideDirective.Model}' is not installed.",
                    overrideHonored: false,
                    routedPrompt));
            }

            if (hasImage &&
                !VisionModelCapabilityRegistry.SupportsImages(
                    knownModel,
                    new VisionSettings(
                        request.Assignments
                            .Where(assignment =>
                                assignment.Category == ModelRoutingCategory.Vision)
                            .Select(assignment => assignment.Model)
                            .ToArray())))
            {
                return Task.FromResult(ChooseCategory(
                    request,
                    installed,
                    hasImage,
                    hasEditorContext,
                    $"Requested model '{knownModel}' cannot accept images.",
                    overrideHonored: false,
                    routedPrompt));
            }

            return Task.FromResult(new ModelRoutingDecision(
                knownModel,
                hasImage
                    ? ModelRoutingCategory.Vision
                    : ModelRoutingCategory.General,
                $"Using requested model: {knownModel}",
                ExplicitOverrideHonored: true,
                FallbackReason: null,
                routedPrompt));
        }

        return Task.FromResult(ChooseCategory(
            request,
            installed,
            hasImage,
            hasEditorContext,
            fallbackPrefix: null,
            overrideHonored: false,
            routedPrompt));
    }

    private static ModelRoutingDecision ChooseCategory(
        ModelRoutingRequest request,
        IReadOnlyList<string> installed,
        bool hasImage,
        bool hasEditorContext,
        string? fallbackPrefix,
        bool overrideHonored,
        string routedPrompt)
    {
        var category = hasImage
            ? ModelRoutingCategory.Vision
            : hasEditorContext || AppearsCodingRelated(routedPrompt)
                ? ModelRoutingCategory.Coding
                : ModelRoutingCategory.General;
        var preferred = FindAssignment(request.Assignments, category);
        var selected = FindInstalled(installed, preferred);
        string? fallbackReason = fallbackPrefix;

        if (selected is null && hasImage)
        {
            selected = installed.FirstOrDefault(model =>
                VisionModelCapabilityRegistry.SupportsImages(
                    model,
                    new VisionSettings(
                        request.Assignments
                            .Where(assignment =>
                                assignment.Category == ModelRoutingCategory.Vision)
                            .Select(assignment => assignment.Model)
                            .ToArray())));
            fallbackReason = CombineFallback(
                fallbackReason,
                selected is null
                    ? "No installed vision-capable model is available."
                    : $"Vision assignment '{preferred}' is unavailable; using '{selected}'.");
        }

        if (selected is null && !hasImage)
        {
            selected = installed.FirstOrDefault();
            fallbackReason = CombineFallback(
                fallbackReason,
                selected is null
                    ? "No installed model is available."
                    : $"Assignment '{preferred}' is unavailable; using '{selected}'.");
        }

        if (selected is null)
        {
            selected = preferred;
        }

        return new ModelRoutingDecision(
            selected,
            category,
            category switch
            {
                ModelRoutingCategory.Vision => $"Using vision model: {selected}",
                ModelRoutingCategory.Coding => $"Using coding model: {selected}",
                ModelRoutingCategory.Fast => $"Using fast model: {selected}",
                ModelRoutingCategory.Reasoning => $"Using reasoning model: {selected}",
                _ => $"Using general model: {selected}"
            },
            overrideHonored,
            fallbackReason,
            routedPrompt);
    }

    private static string FindAssignment(
        IEnumerable<ModelRoutingAssignment> assignments,
        ModelRoutingCategory category)
    {
        return assignments.FirstOrDefault(assignment => assignment.Category == category)
            ?.Model ?? ModelRoutingSettings.DefaultModel;
    }

    private static string? FindInstalled(
        IEnumerable<string> installed,
        string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        return installed.FirstOrDefault(candidate =>
            candidate.Equals(model.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool AppearsCodingRelated(string prompt)
    {
        var signals = new[]
        {
            "code",
            "bug",
            "compile",
            "exception",
            "stack trace",
            "function",
            "class",
            "method",
            "typescript",
            "c#",
            "xaml",
            "sql"
        };

        return signals.Any(signal =>
            prompt.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    private static string? CombineFallback(string? first, string second)
    {
        return string.IsNullOrWhiteSpace(first)
            ? second
            : first + " " + second;
    }
}
