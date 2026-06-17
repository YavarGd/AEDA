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
        var routedPrompt = request.UserPrompt;

        if (!string.IsNullOrWhiteSpace(request.ExplicitModelOverride))
        {
            return Task.FromResult(ChooseOverrideModel(
                request.ExplicitModelOverride,
                ModelRoutingSource.ExplicitOneTurnOverride,
                "Explicit model",
                installed,
                hasImage,
                routedPrompt));
        }

        if (!string.IsNullOrWhiteSpace(request.ConversationModelOverride))
        {
            return Task.FromResult(ChooseOverrideModel(
                request.ConversationModelOverride,
                ModelRoutingSource.ConversationOverride,
                "Conversation override",
                installed,
                hasImage,
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

    private static ModelRoutingDecision ChooseOverrideModel(
        string? requestedModel,
        ModelRoutingSource source,
        string sourceLabel,
        IReadOnlyList<string> installed,
        bool hasImage,
        string routedPrompt)
    {
        var knownModel = FindInstalled(installed, requestedModel);

        if (knownModel is null)
        {
            var requested = string.IsNullOrWhiteSpace(requestedModel)
                ? "unknown"
                : requestedModel.Trim();

            return new ModelRoutingDecision(
                requested,
                hasImage ? ModelRoutingCategory.Vision : ModelRoutingCategory.General,
                $"Model unavailable · {requested}",
                ExplicitOverrideHonored: false,
                FallbackReason: $"Requested model '{requested}' is not installed.",
                routedPrompt)
            {
                IsCapabilityBlocked = true,
                Source = ModelRoutingSource.IncompatibleOverride
            };
        }

        if (hasImage && !VisionModelCapabilityRegistry.SupportsImages(
                knownModel,
                VisionSettings.Default))
        {
            return new ModelRoutingDecision(
                knownModel,
                ModelRoutingCategory.Vision,
                "Model incompatible with image",
                ExplicitOverrideHonored: source == ModelRoutingSource.ExplicitOneTurnOverride,
                FallbackReason: $"{knownModel} cannot analyze images.",
                routedPrompt)
            {
                IsCapabilityBlocked = true,
                Source = ModelRoutingSource.IncompatibleOverride
            };
        }

        return new ModelRoutingDecision(
            knownModel,
            hasImage ? ModelRoutingCategory.Vision : ModelRoutingCategory.General,
            $"{sourceLabel} · {knownModel}",
            ExplicitOverrideHonored: source == ModelRoutingSource.ExplicitOneTurnOverride,
            FallbackReason: null,
            routedPrompt)
        {
            Source = source
        };
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
        var capabilityBlocked = false;

        if (selected is not null &&
            hasImage &&
            !VisionModelCapabilityRegistry.SupportsImages(
                selected,
                VisionSettings.Default))
        {
            selected = null;
            fallbackReason = CombineFallback(
                fallbackReason,
                $"Vision assignment '{preferred}' cannot accept images.");
        }

        if (selected is null && hasImage)
        {
            selected = installed.FirstOrDefault(model =>
                VisionModelCapabilityRegistry.SupportsImages(
                    model,
                    VisionSettings.Default));
            fallbackReason = CombineFallback(
                fallbackReason,
                selected is null
                    ? "No installed vision-capable model is available."
                    : $"Vision assignment '{preferred}' is unavailable; using '{selected}'.");
            capabilityBlocked = selected is null;
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
                ModelRoutingCategory.Vision => $"Automatic · Vision: {selected}",
                ModelRoutingCategory.Coding => $"Automatic · Coding: {selected}",
                ModelRoutingCategory.Fast => $"Settings · Fast: {selected}",
                ModelRoutingCategory.Reasoning => $"Settings · Reasoning: {selected}",
                _ => $"Automatic · General: {selected}"
            },
            overrideHonored,
            fallbackReason,
            routedPrompt)
        {
            IsCapabilityBlocked = capabilityBlocked,
            Source = capabilityBlocked
                ? ModelRoutingSource.IncompatibleOverride
                : fallbackReason is null
                    ? ModelRoutingSource.SettingsOverride
                    : ModelRoutingSource.SafeFallback
        };
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
